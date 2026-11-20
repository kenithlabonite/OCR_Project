$apiKey = 'AIzaSyAqmzBYYNSF9sLYakbkMVxBmW89OPQNNPQ'
$url = "https://generativelanguage.googleapis.com/v1beta/models?key=$apiKey"
$resp = Invoke-RestMethod -Uri $url -Method Get
$resp.models | Where-Object { $_.supportedGenerationMethods -contains 'generateContent' } | Select-Object name, displayName
