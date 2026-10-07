$h = @{ Authorization = "Basic " + [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("guest:guest")) }
$b = "http://localhost:15672/api"
$api = "http://localhost:5102/orders/v1/orders"

function Send-Inventory($routingKey, $payload, $cid = "demo-cid") {
  $body = @{
    properties = @{ headers = @{ "X-Correlation-ID" = $cid }; delivery_mode = 2 }
    routing_key = $routingKey
    payload = ($payload | ConvertTo-Json -Compress)
    payload_encoding = "string"
  } | ConvertTo-Json -Depth 5
  Invoke-RestMethod -Method Post -Uri "$b/exchanges/%2F/store.events/publish" -Headers $h -ContentType "application/json" -Body $body
}

function Place($force = $false) {
  Invoke-RestMethod -Method Post -Uri $api -ContentType "application/json" -Body (@{
    customerName = "Test"; forcePaymentFailure = $force; lines = @(@{ productId = 1; quantity = 2 })
  } | ConvertTo-Json -Depth 5)
}
