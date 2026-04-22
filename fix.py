import os
import re

directories = [
    r"h:\KasaYonetim_New\src\KasaManager.Application\Services",
    r"h:\KasaYonetim_New\src\KasaManager.Web\Controllers",
    r"h:\KasaYonetim_New\src\KasaManager.Web\Models"
]

replacements = {
    "KasaDraftService": "Draft.Helpers.DecimalParsingHelper.TryParseFromTurkish",
    "FormulaEngineService.cs": "Draft.Helpers.DecimalParsingHelper.TryParseFromJson",
    "IntermediateLiveUstRaporProvider.cs": "Draft.Helpers.DecimalParsingHelper.TryParseFromJson",
    "KasaUstRaporController.cs": "Application.Services.Draft.Helpers.DecimalParsingHelper.TryParseFromTurkish",
    "ParityKeyNormalizer.cs": "Application.Services.Draft.Helpers.DecimalParsingHelper.TryParseFromTurkish"
}

for d in directories:
    if not os.path.exists(d): continue
    for root, dirs, files in os.walk(d):
        for file in files:
            if file.endswith(".cs"):
                filepath = os.path.join(root, file)
                with open(filepath, "r", encoding="utf-8") as f:
                    content = f.read()

                # Remove private static bool TryParseDecimal definition
                content = re.sub(r'private\s+static\s+bool\s+TryParseDecimal\s*\([^)]*\)\s*\{[^}]*return[^}]*\}', '', content, flags=re.MULTILINE|re.DOTALL)
                
                # Formula engine parser is larger, so simpler matching for its block:
                content = re.sub(r'private\s+static\s+bool\s+TryParseDecimal\s*\([^)]*\).*?return\s+false;\s*\}', '', content, flags=re.MULTILINE|re.DOTALL)
                
                # ParityKeyNormalizer has a public parser
                content = re.sub(r'public\s+static\s+bool\s+TryParseDecimal\s*\([^)]*\)\s*\{[^}]*return[^}]*\}', '', content, flags=re.MULTILINE|re.DOTALL)

                # Determine correct replacement
                repl = None
                for key, val in replacements.items():
                    if key in file:
                        repl = val
                        break
                
                if repl and "TryParseDecimal(" in content:
                    content = re.sub(r'\bTryParseDecimal\(', repl + '(', content)

                with open(filepath, "w", encoding="utf-8") as f:
                    f.write(content)

print("Done")
