#!/usr/bin/env pwsh

# API Test Script
$baseUrl = "http://localhost:5245/api"
$errorCount = 0
$successCount = 0

function Test-Endpoint {
	param(
		[string]$name,
		[string]$method,
		[string]$endpoint,
		[object]$body = $null
	)

	Write-Host "`n📝 Testing: $name" -ForegroundColor Cyan
	Write-Host "   $method $endpoint" -ForegroundColor Gray

	try {
		$params = @{
			Uri         = "$baseUrl$endpoint"
			Method      = $method
			ErrorAction = 'Stop'
		}

		if ($body) {
			$params['Body'] = $body | ConvertTo-Json -Depth 10
			$params['ContentType'] = 'application/json'
		}

		$response = Invoke-WebRequest @params
		$data = $response.Content | ConvertFrom-Json

		Write-Host "   ✅ Status: $($response.StatusCode)" -ForegroundColor Green
		Write-Host "   📦 Response: $($data | ConvertTo-Json -Depth 2 -Compress)" -ForegroundColor Green

		$script:successCount++
		return $data
	}
	catch {
		Write-Host "   ❌ Error: $($_.Exception.Message)" -ForegroundColor Red
		$script:errorCount++
		return $null
	}
}

Write-Host "================================" -ForegroundColor Yellow
Write-Host "  🛒 ORDER & PAYMENT MICROSERVICE - API TEST" -ForegroundColor Yellow
Write-Host "  Base URL: $baseUrl" -ForegroundColor Yellow
Write-Host "================================`n" -ForegroundColor Yellow

# 1. Test Products API
Write-Host "`n🍽️  ===== PRODUCTS API =====" -ForegroundColor Magenta

Test-Endpoint "GET All Products" "GET" "/products"

Test-Endpoint "GET Products by Category" "GET" "/products/category/Food"

Test-Endpoint "GET Product by ID" "GET" "/products/1"

# 2. Test Users API
Write-Host "`n👤 ===== USERS API =====" -ForegroundColor Magenta

$registerData = @{
	email = "customer@example.com"
	password = "Test@123456"
	fullName = "Nguyễn Văn Đơn"
	phoneNumber = "0901234567"
	address = "123 Đường Lê Lợi, TP.HCM"
	city = "Ho Chi Minh"
	postalCode = "70000"
}

$registerResult = Test-Endpoint "Register User" "POST" "/users/register" $registerData

if ($registerResult) {
	$userId = $registerResult.id
	Write-Host "   💡 Created User ID: $userId" -ForegroundColor Cyan

	Test-Endpoint "Login User" "POST" "/users/login" @{
		email = $registerData.email
		password = $registerData.password
	}

	Test-Endpoint "Get User by ID" "GET" "/users/$userId"
}

# 3. Test Cart API
Write-Host "`n🛒 ===== SHOPPING CART API =====" -ForegroundColor Magenta

if ($userId) {
	$cartResult = Test-Endpoint "Get/Create Cart for User" "GET" "/cart/user/$userId"

	if ($cartResult) {
		$cartId = $cartResult.id
		Write-Host "   💡 Cart ID: $cartId" -ForegroundColor Cyan

		# Add items to cart
		Test-Endpoint "Add Item to Cart" "POST" "/cart/user/$userId/add" @{
			productId = 1
			quantity = 2
		}

		Test-Endpoint "Add Another Item" "POST" "/cart/user/$userId/add" @{
			productId = 2
			quantity = 1
		}

		# View updated cart
		Test-Endpoint "Get Updated Cart" "GET" "/cart/user/$userId"
	}
}

# 4. Test Orders API
Write-Host "`n📦 ===== ORDERS API =====" -ForegroundColor Magenta

if ($userId) {
	$orderData = @{
		customerId = "CUST$([int]$userId)"
		userId = $userId
		totalAmount = 150000
		status = "Pending"
		items = @(
			@{
				productId = 1
				quantity = 2
				unitPrice = 55000
			},
			@{
				productId = 3
				quantity = 1
				unitPrice = 40000
			}
		)
	}

	$orderResult = Test-Endpoint "Create Order" "POST" "/orders" $orderData

	if ($orderResult) {
		$orderId = $orderResult.id
		Write-Host "   💡 Order ID: $orderId" -ForegroundColor Cyan

		Test-Endpoint "Get Order by ID" "GET" "/orders/$orderId"

		# 5. Test Payments API
		Write-Host "`n💳 ===== PAYMENTS API =====" -ForegroundColor Magenta

		$paymentData = @{
			orderId = $orderId
			amount = $orderResult.totalAmount
			method = "CreditCard"
			status = "Completed"
		}

		Test-Endpoint "Process Payment" "POST" "/payments" $paymentData

		Test-Endpoint "Get Payments for Order" "GET" "/payments/order/$orderId"
	}
}

# Summary
Write-Host "`n================================" -ForegroundColor Yellow
Write-Host "📊 TEST SUMMARY" -ForegroundColor Yellow
Write-Host "================================" -ForegroundColor Yellow
Write-Host "✅ Success: $successCount" -ForegroundColor Green
Write-Host "❌ Failed: $errorCount" -ForegroundColor $(if ($errorCount -gt 0) { 'Red' } else { 'Green' })
Write-Host "================================" -ForegroundColor Yellow
