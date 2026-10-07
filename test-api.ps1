#!/usr/bin/env pwsh

param(
    [string]$BaseUrl = "http://localhost:5246/api"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$successCount = 0
$errorCount = 0

function Invoke-ApiCheck {
    param(
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [string]$Method,
        [Parameter(Mandatory)] [string]$Endpoint,
        [object]$Body,
        [hashtable]$Headers = @{},
        [int[]]$ExpectedStatus = @(200)
    )

    Write-Host "`nTesting: $Name" -ForegroundColor Cyan
    Write-Host "  $Method $Endpoint" -ForegroundColor DarkGray

    try {
        $parameters = @{
            Uri = "$BaseUrl$Endpoint"
            Method = $Method
            Headers = $Headers
            SkipHttpErrorCheck = $true
        }
        if ($null -ne $Body) {
            $parameters.Body = $Body | ConvertTo-Json -Depth 10
            $parameters.ContentType = "application/json"
        }

        $response = Invoke-WebRequest @parameters
        $data = if ([string]::IsNullOrWhiteSpace($response.Content)) {
            $null
        } else {
            $response.Content | ConvertFrom-Json
        }

        if ($response.StatusCode -notin $ExpectedStatus) {
            $script:errorCount++
            Write-Host "  FAIL: HTTP $($response.StatusCode), expected $($ExpectedStatus -join '/')" -ForegroundColor Red
            if ($null -ne $data) {
                Write-Host "  Response: $($data | ConvertTo-Json -Depth 4 -Compress)" -ForegroundColor Red
            }
            return @{ StatusCode = $response.StatusCode; Data = $data; Passed = $false }
        }

        $script:successCount++
        Write-Host "  PASS: HTTP $($response.StatusCode)" -ForegroundColor Green
        return @{ StatusCode = $response.StatusCode; Data = $data; Passed = $true }
    }
    catch {
        $script:errorCount++
        Write-Host "  ERROR: $($_.Exception.Message)" -ForegroundColor Red
        return @{ StatusCode = 0; Data = $null; Passed = $false }
    }
}

Write-Host "============================================" -ForegroundColor Yellow
Write-Host " ORDER & PAYMENT - AUTHENTICATED E2E CHECK" -ForegroundColor Yellow
Write-Host " $BaseUrl" -ForegroundColor Yellow
Write-Host "============================================" -ForegroundColor Yellow

$productsResult = Invoke-ApiCheck -Name "List seeded products" -Method GET -Endpoint "/products"
if (-not $productsResult.Passed -or @($productsResult.Data).Count -eq 0) {
    Write-Host "`nAPI is unavailable or no seeded product exists." -ForegroundColor Red
    exit 1
}
$product = @($productsResult.Data)[0]

$unique = "$(Get-Date -Format yyyyMMddHHmmss)-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
$email = "e2e-$unique@example.test"
$password = "E2e-Test-Password!123"

$registration = Invoke-ApiCheck -Name "Register customer" -Method POST -Endpoint "/users/register" -ExpectedStatus 201 -Body @{
    email = $email
    password = $password
    fullName = "E2E Test Customer"
    phoneNumber = "0900000000"
}
if (-not $registration.Passed) { exit 1 }
$userId = [int]$registration.Data.id

$login = Invoke-ApiCheck -Name "Login and obtain JWT" -Method POST -Endpoint "/users/login" -Body @{
    email = $email
    password = $password
}
if (-not $login.Passed) { exit 1 }
$authHeaders = @{ Authorization = "Bearer $($login.Data.token)" }

Invoke-ApiCheck -Name "Read own profile" -Method GET -Endpoint "/users/$userId" -Headers $authHeaders | Out-Null
Invoke-ApiCheck -Name "Create/read own cart" -Method GET -Endpoint "/cart/user/$userId" -Headers $authHeaders | Out-Null
Invoke-ApiCheck -Name "Add seeded product to cart" -Method POST -Endpoint "/cart/user/$userId/add" -Headers $authHeaders -Body @{
    productId = [int]$product.id
    quantity = 2
} | Out-Null

$quantity = 2
$total = [decimal]$product.price * $quantity
$order = Invoke-ApiCheck -Name "Create order with server price" -Method POST -Endpoint "/orders" -Headers $authHeaders -ExpectedStatus 201 -Body @{
    totalAmount = $total
    address = "1 E2E Street"
    city = "Ho Chi Minh City"
    items = @(
        @{
            productId = [int]$product.id
            quantity = $quantity
            unitPrice = [decimal]$product.price
        }
    )
}
if (-not $order.Passed) { exit 1 }
$orderId = [int]$order.Data.id

$idempotencyKey = "charge-$([guid]::NewGuid().ToString('N'))"
$paymentHeaders = @{
    Authorization = $authHeaders.Authorization
    "Idempotency-Key" = $idempotencyKey
}
$paymentBody = @{
    orderId = $orderId
    amount = [decimal]$order.Data.totalAmount
}
$payment = Invoke-ApiCheck -Name "Charge order" -Method POST -Endpoint "/payments" -Headers $paymentHeaders -ExpectedStatus 201 -Body $paymentBody
if (-not $payment.Passed) { exit 1 }
$paymentId = [int]$payment.Data.id

Invoke-ApiCheck -Name "Repeat charge idempotently" -Method POST -Endpoint "/payments" -Headers $paymentHeaders -ExpectedStatus 201 -Body $paymentBody | Out-Null
Invoke-ApiCheck -Name "Read payment" -Method GET -Endpoint "/payments/$paymentId" -Headers $authHeaders | Out-Null
$mine = Invoke-ApiCheck -Name "Read own orders" -Method GET -Endpoint "/orders/mine" -Headers $authHeaders

$paidOrder = @($mine.Data) | Where-Object { [int]$_.id -eq $orderId }
if (@($paidOrder).Count -ne 1 -or $paidOrder.status -ne "PAID") {
    $errorCount++
    Write-Host "  FAIL: order $orderId is not uniquely visible in PAID state." -ForegroundColor Red
} else {
    $successCount++
    Write-Host "  PASS: order $orderId reached PAID and remains RESERVED." -ForegroundColor Green
}

Invoke-ApiCheck -Name "Reject cancellation after successful payment" -Method POST -Endpoint "/orders/$orderId/cancel" -Headers $authHeaders -ExpectedStatus 400 | Out-Null

Write-Host "`n============================================" -ForegroundColor Yellow
Write-Host "E2E SUMMARY: $successCount passed, $errorCount failed" -ForegroundColor $(if ($errorCount -eq 0) { "Green" } else { "Red" })
Write-Host "User=$userId Order=$orderId Payment=$paymentId" -ForegroundColor Yellow
Write-Host "============================================" -ForegroundColor Yellow

if ($errorCount -gt 0) { exit 1 }
