$imgPath = 'C:\Users\kenit\.gemini\antigravity\brain\c109275e-9a66-418d-ae31-bbabe2be80a2\.user_uploaded\media_1795077073547_99018899.jpg'
$bytes = [System.IO.File]::ReadAllBytes($imgPath)
$base64 = [Convert]::ToBase64String($bytes)
$prompt = 'You are an exam-grading assistant. Extract every question number and the answer letter (A-D) from the attached answer sheet. Return ONLY a JSON object in the exact format {"answers": {"1":"A","2":"B","3":"C","4":"D","5":"B","6":"B"}}. Do not include any extra text.'
$payloadObj = @{contents = @(@{text = $prompt}, @{inline_data = @{mime_type = "image/jpeg"; data = $base64}})}
$payload = $payloadObj | ConvertTo-Json -Depth 5
$apiKey = 'AIzaSyAqmzBYYNSF9sLYakbkMVxBmW89OPQNNPQ'
$uri = "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.8-flash:generateContent?key=$apiKey"
Invoke-RestMethod -Method Post -Uri $uri -ContentType 'application/json' -Body $payload -TimeoutSec 300