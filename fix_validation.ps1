$filePath = 'C:\Users\kenit\OCR_Project\Project_OLMS\Project_OLMS\Services\GeminiService.cs'
$lines = [System.IO.File]::ReadAllLines($filePath)

Write-Host "Total lines: $($lines.Count)"

# Find the two validation blocks by searching for the comment line
$blockStarts = @()
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "Validate that each answer follows") {
        $blockStarts += $i
        Write-Host "Found validation block at line $($i+1)"
    }
}

if ($blockStarts.Count -eq 0) {
    Write-Error "No validation blocks found!"
    exit 1
}

# Process in reverse order so line numbers don't shift
$blockStarts = $blockStarts | Sort-Object -Descending

foreach ($start in $blockStarts) {
    # Find the end of the block: the line with 'return (validatedDict, rawText, null);'
    $end = $start
    for ($j = $start; $j -lt [Math]::Min($start + 30, $lines.Count); $j++) {
        if ($lines[$j] -match 'return \(validatedDict') {
            $end = $j
            break
        }
    }
    Write-Host "Replacing lines $($start+1) to $($end+1)"

    # Build replacement lines
    $replacement = @(
        '                // Trust Gemini Vision directly - skip raw-text re-validation which discards valid answers.',
        '                return (resultDict, rawText, null);'
    )

    # Replace lines from $start to $end (inclusive) with the replacement
    $newLines = $lines[0..($start-1)] + $replacement + $lines[($end+1)..($lines.Count-1)]
    $lines = $newLines
}

[System.IO.File]::WriteAllLines($filePath, $lines, [System.Text.UTF8Encoding]::new($false))
Write-Host "Done! File saved."
