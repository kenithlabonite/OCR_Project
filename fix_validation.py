path = r'C:\Users\kenit\OCR_Project\Project_OLMS\Project_OLMS\Services\GeminiService.cs'
with open(path, 'rb') as f:
    content = f.read().decode('utf-8')

# The validation block appears twice (single-page and multi-page).
# Replace both with a simple return that trusts Gemini Vision directly.
old_block = (
    '                // Validate that each answer follows the \'answer before number\' rule using the raw extracted text.\r\n'
    'var validatedDict = new Dictionary<int, string>();\r\n'
    'foreach (var kvp in resultDict)\r\n'
    '{\r\n'
    '    int qNum = kvp.Key;\r\n'
    '    string ans = kvp.Value;\r\n'
    '    // Build regex for answer-first pattern, allowing optional punctuation and whitespace.\r\n'
    '    var pattern = $"\\\\b{Regex.Escape(ans)}\\\\s*[\\\\.\\\\_ \\\\-\\\\)]*\\\\s*{qNum}\\\\b";\r\n'
    '    if (Regex.IsMatch(rawText, pattern, RegexOptions.IgnoreCase))\r\n'
    '    {\r\n'
    '        validatedDict[qNum] = ans;\r\n'
    '    }\r\n'
    '    else\r\n'
    '    {\r\n'
    '        _logger.LogWarning("[GeminiService] Discarded answer for question {0} because it does not appear before the number in raw text.", qNum);\r\n'
    '    }\r\n'
    '}\r\n'
    'return (validatedDict, rawText, null);\r\n'
)

new_block = (
    '                // Trust Gemini Vision directly – skip raw-text re-validation which discards valid answers.\r\n'
    '                return (resultDict, rawText, null);\r\n'
)

count = content.count(old_block)
print(f'Found {count} occurrence(s) of the validation block.')

if count == 0:
    # Try to show what the block looks like in the file
    idx = content.find('// Validate that each answer follows')
    print('Actual text at that position:')
    print(repr(content[idx:idx+600]))
else:
    fixed = content.replace(old_block, new_block)
    with open(path, 'wb') as f:
        f.write(fixed.encode('utf-8'))
    print('Fixed successfully.')
