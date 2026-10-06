import sys
from pathlib import Path
from pypdf import PdfReader

pdf_path = Path(__file__).resolve().parent / "Trabajo 1.pdf"

if not pdf_path.exists():
    print(f"File not found: {pdf_path}", file=sys.stderr)
    sys.exit(1)

reader = PdfReader(pdf_path)

output_lines = [
    f"# {pdf_path.stem}\n",
    f"> Total pages: {len(reader.pages)}\n"
]

for i, page in enumerate(reader.pages, start=1):
    output_lines.append(f"## Page {i}\n")
    text = page.extract_text()
    if text:
        output_lines.append(text.strip() + "\n")
    else:
        output_lines.append("_No text found or scanned image_\n")

content = "\n".join(output_lines)

# Save to markdown file
output_md = pdf_path.parent / "assignment_1.md"
output_md.write_text(content, encoding="utf-8")

print(content)
print(f"\n[Saved to {output_md.name}]", file=sys.stderr)

