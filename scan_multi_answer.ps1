$apiKey = 'AIzaSyAqmzBYYNSF9sLYakbkMVxBmW89OPQNNPQ'
$prompt = @'
You are an exam‑grading assistant. The attached images together form a multiple‑choice answer sheet.
Each answer is a single capital letter (A‑D) written inside a bubble next to the question number (e.g., "B 12").
Extract every question‑number → answer pair and return ONLY a JSON object in this exact format:
{ "answers": { "1":"A", "2":"B", "3":"C", "4":"D", "5":"B", "6":"B", "7":"A", "8":"C", "9":"D", "10":"A" } }
Do not include any explanations, surrounding text, or markup.
'@

# Paths to the three uploaded images
$paths = @(
    'C:\Users\kenit\.gemini\antigravity\brain\c109275e-9a66-418d-ae31-bbabe2be80a2\.user_uploaded\media_1795084833799_d40a588d.jpg',
    'C:\Users\kenit\.gemini\antigravity\brain\c109275e-9a66-418d-ae31-bbabe2be80a2\.user_uploaded\media_1795084833807_5637ee0f.jpg',
    'C:\Users\kenit\.gemini\antigravity\brain\c109275e-9a66-418d-ae31-bbabe2be80a2\.user_uploaded\media_1795085032478_44ca5580.png'
)

$contents = @(@{ text = $prompt })
foreach ($p in $paths) {
    $bytes = [System.IO.File]::ReadAllBytes($p)
    $b64 = [Convert]::ToBase64String($bytes)
    $mime = if ($p -like '*.png') { 'image/png' } else { 'image/jpeg' }
    $contents += @{ inline_data = @{ mime_type = $mime; data = $b64 } }
}

$payloadObj = @{ contents = $contents }
$payload = $payloadObj | ConvertTo-Json -Depth 6
$uri = "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.8-flash:generateContent?key=$apiKey"

try {
    $resp = Invoke-RestMethod -Method Post -Uri $uri -ContentType 'application/json' -Body $payload -TimeoutSec 300
    $resp | ConvertTo-Json -Depth 10
} catch {
    Write-Error "Call failed: $_"
}
