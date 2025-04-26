import os
import sys
import re

# Path to EasyOCR library
easyocr_path = None

# Find path to EasyOCR library
for path in sys.path:
    utils_path = os.path.join(path, 'easyocr', 'utils.py')
    if os.path.exists(utils_path):
        easyocr_path = utils_path
        break

if easyocr_path:
    print(f"Found EasyOCR at: {easyocr_path}")
    
    # Read file content
    with open(easyocr_path, 'r', encoding='utf-8') as file:
        content = file.read()
    
    # Check if ANTIALIAS attribute exists
    if 'ANTIALIAS' in content:
        # Replace ANTIALIAS with LANCZOS
        new_content = content.replace('Image.ANTIALIAS', 'Image.LANCZOS')
        
        # Save modified file
        with open(easyocr_path, 'w', encoding='utf-8') as file:
            file.write(new_content)
        
        print("Fixed ANTIALIAS issue in EasyOCR. Please restart the application.")
    else:
        print("ANTIALIAS issue not found in EasyOCR utils.py file.")
else:
    print("EasyOCR library not found in Python path.")