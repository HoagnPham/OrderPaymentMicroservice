#!/bin/bash

echo "🧪 Testing Order & Payment Microservice API"
echo "==========================================="

BASE_URL="http://localhost:5245/api"

echo -e "\n✅ 1. Testing GET /products"
curl -s "$BASE_URL/products" | jq '.' | head -20

echo -e "\n✅ 2. Testing GET /products/1"
curl -s "$BASE_URL/products/1" | jq '.'

echo -e "\n✅ 3. Testing POST /users/register"
curl -s -X POST "$BASE_URL/users/register" \
  -H "Content-Type: application/json" \
  -d '{
	"email": "test@example.com",
	"password": "Test@123",
	"fullName": "Test User",
	"phoneNumber": "0912345678",
	"address": "123 Main St",
	"city": "HCM",
	"postalCode": "70000"
  }' | jq '.'

echo -e "\n✅ API Test Complete"
