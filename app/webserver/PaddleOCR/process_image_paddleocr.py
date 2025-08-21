import os
import json
import time
import numpy as np
import tempfile
from PIL import Image, ImageEnhance, ImageFilter
from paddleocr import PaddleOCR
# import torch

# Global variables to manage OCR engine
OCR_ENGINE = None
CURRENT_LANG = None

def initialize_ocr_engine(lang='en'):
    """
    Initialize or reinitialize the OCR engine with the specified language.
    
    Args:
        lang (str): Language to use for OCR (default: 'en')
    
    Returns:
        PaddleOCR: Initialized OCR engine
    """
    global OCR_ENGINE, CURRENT_LANG

    # Map language codes to PaddleOCR language codes
    lang_map = {
        'ja': 'japan',
        'ko': 'korean',
        'en': 'en',
        'ch_sim': 'ch',
        'fr': 'fr',
        'ru': 'ru',
        'de': 'german',
        'es': 'es',
        'it': 'it',
        'hi': 'hi',
        'pt': 'pt',
        'ar': 'ar',
        'nl': 'nl',
        'pl': 'pl',
        'ro': 'ro',
        'fa': 'fa',
        'cs': 'cs',
        'id': 'id',
        'th': 'th',
        'ch_tra': 'ch_cht'
    }

    # Use mapped language or default to input if not in map
    paddle_lang = lang_map.get(lang, lang)

    # Only reinitialize if language has changed
    if OCR_ENGINE is None or CURRENT_LANG != lang:
        print(f"Initializing PaddleOCR engine with language: {paddle_lang}...")
        start_time = time.time()

        # Initialize PaddleOCR with the specified language and new parameters
        OCR_ENGINE = PaddleOCR(
            use_doc_orientation_classify=False,
            use_doc_unwarping=False,
            use_textline_orientation=False,
            lang=paddle_lang
        )
        CURRENT_LANG = lang
        initialization_time = time.time() - start_time
        print(f"PaddleOCR initialization completed in {initialization_time:.2f} seconds")
        flag_file = os.path.join(tempfile.gettempdir(), "paddleocr_ready.txt")
        with open(flag_file, "w") as f:
            f.write("READY")
        print("Ready flag created!")
    else:
        print(f"Using existing PaddleOCR engine with language: {paddle_lang}")

    return OCR_ENGINE

def release_gpu_resources():
    # if torch.cuda.is_available():
    #     torch.cuda.empty_cache()
    print("Released GPU resources after OCR processing")

def preprocess_image(image):
    """
    Preprocess the image to improve OCR performance.
    This includes converting to grayscale, enhancing contrast, and reducing noise.
    
    Args:
        image (PIL.Image): Input image.
    
    Returns:
        PIL.Image: Preprocessed image.
    """
    # Convert image to grayscale
    image = image.convert('L')
    
    # Enhance contrast
    enhancer = ImageEnhance.Contrast(image)
    image = enhancer.enhance(2.0)
    
    # Apply a median filter for noise reduction
    image = image.filter(ImageFilter.MedianFilter(size=3))
    
    # Convert back to RGB (PaddleOCR may expect an RGB image)
    return image.convert('RGB')

def upscale_image(image, min_width=1024, min_height=768):
    """
    Upscale the image if its dimensions are below a specified threshold.
    
    Args:
        image (PIL.Image): Input image.
        min_width (int): Minimum desired width.
        min_height (int): Minimum desired height.
    
    Returns:
        tuple: (PIL.Image, float) Upscaled image if necessary, else original image, and the scale factor.
    """
    width, height = image.size
    scale = 1.0
    if width < min_width or height < min_height:
        scale = max(min_width / width, min_height / height)
        new_size = (int(width * scale), int(height * scale))
        print(f"Upscaling image from ({width}, {height}) to {new_size}")
        # Use LANCZOS instead of ANTIALIAS which is deprecated in newer PIL
        image = image.resize(new_size, Image.LANCZOS)
    return image, scale

# Initialize with default language at module load time
initialize_ocr_engine('en')

def process_image(image_path, lang='en', preprocess_images=False, upscale_if_needed=False, char_level="True"):
    """
    Process an image using PaddleOCR and return the OCR results.
    
    Args:
        image_path (str): Path to the image to process.
        lang (str): Language to use for OCR (default: 'en').
        preprocess_images (bool): Flag to determine whether to preprocess the image.
        upscale_if_needed (bool): Flag to determine whether to upscale the image if it's low resolution.
        char_level (bool): If True, split text into characters with their estimated positions.
    
    Returns:
        dict: JSON-serializable dictionary with OCR results.
    """
    # Check if image exists
    if not os.path.exists(image_path):
        return {"error": f"Image file not found: {image_path}"}

    try:
        # Start timing the OCR process
        start_time = time.time()
        
        # Open the image using PIL
        image = Image.open(image_path)
        
        # Preprocess image if the flag is set
        if preprocess_images:
            print("Preprocessing image...")
            image = preprocess_image(image)
        
        # Upscale image if the flag is set, and get the scale factor
        scale = 1.0
        if upscale_if_needed:
            print("Checking if upscaling is needed...")
            image, scale = upscale_image(image)
        
        # Ensure OCR engine is initialized with the correct language
        ocr_engine = initialize_ocr_engine(lang)
        
        # Save the image temporarily if it's been modified
        temp_image_path = image_path
        if preprocess_images or (upscale_if_needed and scale != 1.0):
            temp_image_path = f"{os.path.splitext(image_path)[0]}_temp.png"
            image.save(temp_image_path)
            print(f"Saved preprocessed image to {temp_image_path}")
        
        # Use the initialized OCR engine
        result = ocr_engine.predict(temp_image_path)
        print(f"OCR results received. Processing...")
        
        # Debug output to understand the result structure
        print(f"Result type: {type(result)}")
        if isinstance(result, list):
            print(f"Result length: {len(result)}")
            if len(result) > 0:
                print(f"First element type: {type(result[0])}")
                # Print the keys if it's a dictionary
                if isinstance(result[0], dict):
                    print(f"Dictionary keys: {result[0].keys()}")
        
        # Remove temporary file if created
        if temp_image_path != image_path and os.path.exists(temp_image_path):
            os.remove(temp_image_path)
            print(f"Removed temporary file: {temp_image_path}")
        
        # Calculate processing time
        processing_time = time.time() - start_time
        
        # Prepare the results
        ocr_results = []
        
        # Process the result based on its structure
        try:
            # Handle the OCRResult format from the log - direct dictionary with rec_texts, rec_polys, rec_scores
            if isinstance(result, list) and len(result) > 0 and isinstance(result[0], dict):
                for item in result:
                    # Check if the dictionary has the required keys directly
                    if 'rec_texts' in item and 'rec_polys' in item and 'rec_scores' in item:
                        texts = item['rec_texts']
                        polys = item['rec_polys']
                        scores = item['rec_scores']
                        
                        print(f"Found {len(texts)} text items")
                        
                        # Process each text detection
                        for i in range(len(texts)):
                            if i < len(polys) and i < len(scores):
                                text = texts[i]
                                poly = polys[i]
                                confidence = float(scores[i]) if i < len(scores) else 0.0
                                
                                # Convert NumPy arrays to lists if needed
                                if hasattr(poly, 'tolist'):
                                    poly = poly.tolist()
                                
                                # Convert all coordinates to float for JSON serialization
                                try:
                                    box_native = [[float(coord) for coord in point] for point in poly]
                                except Exception as e:
                                    print(f"Error converting poly to box_native: {e}")
                                    print(f"Poly: {poly}")
                                    # Create a default box if conversion fails
                                    box_native = [[0, 0], [100, 0], [100, 30], [0, 30]]
                                
                                # Apply scaling if image was upscaled
                                if scale != 1.0:
                                    box_native = [[coord / scale for coord in point] for point in box_native]
                                
                                if char_level == 'True' and len(text) > 1:
                                    # Estimate character positions
                                    char_results = split_into_characters(text, box_native, confidence)
                                    ocr_results.extend(char_results)
                                else:
                                    # Keep the original word-level detection
                                    ocr_results.append({
                                        "rect": box_native,
                                        "text": text,
                                        "confidence": confidence,
                                        "is_character": False
                                    })
                    
                    # Handle the case where result is a dictionary with 'res' key
                    elif 'res' in item:
                        res = item['res']
                        
                        # Check if we have recognized texts and polygons
                        if 'rec_texts' in res and 'rec_polys' in res and 'rec_scores' in res:
                            texts = res['rec_texts']
                            polys = res['rec_polys']
                            scores = res['rec_scores']
                            
                            print(f"Found {len(texts)} text items in 'res'")
                            
                            # Process each text detection
                            for i in range(len(texts)):
                                if i < len(polys) and i < len(scores):
                                    text = texts[i]
                                    poly = polys[i]
                                    confidence = float(scores[i]) if i < len(scores) else 0.0
                                    
                                    # Convert NumPy arrays to lists if needed
                                    if hasattr(poly, 'tolist'):
                                        poly = poly.tolist()
                                    
                                    # Convert all coordinates to float for JSON serialization
                                    try:
                                        box_native = [[float(coord) for coord in point] for point in poly]
                                    except Exception as e:
                                        print(f"Error converting poly to box_native: {e}")
                                        print(f"Poly: {poly}")
                                        # Create a default box if conversion fails
                                        box_native = [[0, 0], [100, 0], [100, 30], [0, 30]]
                                    
                                    # Apply scaling if image was upscaled
                                    if scale != 1.0:
                                        box_native = [[coord / scale for coord in point] for point in box_native]
                                    
                                    if char_level == 'True' and len(text) > 1:
                                        # Estimate character positions
                                        char_results = split_into_characters(text, box_native, confidence)
                                        ocr_results.extend(char_results)
                                    else:
                                        # Keep the original word-level detection
                                        ocr_results.append({
                                            "rect": box_native,
                                            "text": text,
                                            "confidence": confidence,
                                            "is_character": False
                                        })
            
            # Handle the case where result is a list (standard PaddleOCR output)
            elif isinstance(result, list) and len(result) > 0 and isinstance(result[0], list):
                for line in result[0]:
                    try:
                        # Each line contains coordinates and text with confidence
                        # Format: [[[x1,y1],[x2,y2],[x3,y3],[x4,y4]], [text, confidence]]
                        if len(line) < 2:
                            print(f"Warning: Line doesn't have enough elements: {line}")
                            continue
                            
                        box = line[0]
                        
                        # Handle different result formats
                        if isinstance(line[1], list) and len(line[1]) >= 2:
                            text = line[1][0]
                            confidence = float(line[1][1])
                        elif isinstance(line[1], str):
                            text = line[1]
                            confidence = 0.0  # Default confidence if not provided
                        else:
                            print(f"Warning: Unexpected result format: {line}")
                            continue
                        
                        # Convert coordinates back to the original image scale if upscaled
                        if scale != 1.0:
                            box = [[coord / scale for coord in point] for point in box]
                        
                        # Convert all NumPy types to native Python types for JSON serialization
                        box_native = [[float(coord) for coord in point] for point in box]
                        
                        if char_level == 'True' and len(text) > 1:
                            # Estimate character positions
                            char_results = split_into_characters(text, box_native, confidence)
                            ocr_results.extend(char_results)
                        else:
                            # Keep the original word-level detection
                            ocr_results.append({
                                "rect": box_native,
                                "text": text,
                                "confidence": confidence,
                                "is_character": False
                            })
                    except Exception as e:
                        print(f"Error processing line: {e}")
                        import traceback
                        traceback.print_exc()
                        continue
            
            # Handle the case where result is a dictionary with 'res' key
            elif isinstance(result, dict) and 'res' in result:
                res = result['res']
                
                # Check if we have recognized texts and polygons
                if isinstance(res, dict) and 'rec_texts' in res and 'rec_polys' in res and 'rec_scores' in res:
                    texts = res['rec_texts']
                    polys = res['rec_polys']
                    scores = res['rec_scores']
                    
                    # Process each text detection
                    for i in range(len(texts)):
                        if i < len(polys) and i < len(scores):
                            text = texts[i]
                            poly = polys[i]
                            confidence = float(scores[i]) if i < len(scores) else 0.0
                            
                            # Convert NumPy arrays to lists if needed
                            if hasattr(poly, 'tolist'):
                                poly = poly.tolist()
                            
                            # Ensure poly is a list of points (4 corners)
                            if not isinstance(poly[0], list) and len(poly) >= 8:
                                # If poly is a flat list of coordinates, convert to points
                                # Assuming format [x1, y1, x2, y2, x3, y3, x4, y4]
                                poly = [
                                    [poly[0], poly[1]],  # top-left
                                    [poly[2], poly[3]],  # top-right
                                    [poly[4], poly[5]],  # bottom-right
                                    [poly[6], poly[7]]   # bottom-left
                                ]
                            
                            # Convert all coordinates to float for JSON serialization
                            try:
                                box_native = [[float(coord) for coord in point] for point in poly]
                            except Exception as e:
                                print(f"Error converting poly to box_native: {e}")
                                print(f"Poly: {poly}")
                                # Create a default box if conversion fails
                                box_native = [[0, 0], [100, 0], [100, 30], [0, 30]]
                            
                            # Apply scaling if image was upscaled
                            if scale != 1.0:
                                box_native = [[coord / scale for coord in point] for point in box_native]
                            
                            if char_level == 'True' and len(text) > 1:
                                # Estimate character positions
                                char_results = split_into_characters(text, box_native, confidence)
                                ocr_results.extend(char_results)
                            else:
                                # Keep the original word-level detection
                                ocr_results.append({
                                    "rect": box_native,
                                    "text": text,
                                    "confidence": confidence,
                                    "is_character": False
                                })
            
            # If we didn't process any results, try one more approach with OCRResult objects
            if not ocr_results and isinstance(result, list) and len(result) > 0:
                # Try to convert OCRResult objects to dictionaries
                for item in result:
                    try:
                        # Convert the object to a dictionary if it's not already
                        if not isinstance(item, dict):
                            item_dict = vars(item)  # Try to get object attributes as dict
                        else:
                            item_dict = item
                            
                        # Now try to process the dictionary
                        if 'rec_texts' in item_dict and 'rec_polys' in item_dict and 'rec_scores' in item_dict:
                            texts = item_dict['rec_texts']
                            polys = item_dict['rec_polys']
                            scores = item_dict['rec_scores']
                            
                            print(f"Found {len(texts)} text items after conversion")
                            
                            # Process each text detection
                            for i in range(len(texts)):
                                if i < len(polys) and i < len(scores):
                                    text = texts[i]
                                    poly = polys[i]
                                    confidence = float(scores[i]) if i < len(scores) else 0.0
                                    
                                    # Convert NumPy arrays to lists if needed
                                    if hasattr(poly, 'tolist'):
                                        poly = poly.tolist()
                                    
                                    # Convert all coordinates to float for JSON serialization
                                    try:
                                        box_native = [[float(coord) for coord in point] for point in poly]
                                    except Exception as e:
                                        print(f"Error converting poly to box_native: {e}")
                                        print(f"Poly: {poly}")
                                        # Create a default box if conversion fails
                                        box_native = [[0, 0], [100, 0], [100, 30], [0, 30]]
                                    
                                    # Apply scaling if image was upscaled
                                    if scale != 1.0:
                                        box_native = [[coord / scale for coord in point] for point in box_native]
                                    
                                    if char_level == 'True' and len(text) > 1:
                                        # Estimate character positions
                                        char_results = split_into_characters(text, box_native, confidence)
                                        ocr_results.extend(char_results)
                                    else:
                                        # Keep the original word-level detection
                                        ocr_results.append({
                                            "rect": box_native,
                                            "text": text,
                                            "confidence": confidence,
                                            "is_character": False
                                        })
                    except Exception as e:
                        print(f"Error converting OCRResult to dictionary: {e}")
                        import traceback
                        traceback.print_exc()
            
            # If we still didn't process any results, log an error
            if not ocr_results:
                print("Warning: No OCR results were processed. Result format may not be supported.")
                print(f"Result: {result}")
        
        except Exception as e:
            print(f"Error processing OCR results: {str(e)}")
            import traceback
            traceback.print_exc()
        
        return {
            "status": "success",
            "results": ocr_results,
            "processing_time_seconds": float(processing_time),
            "char_level": char_level
        }
    
    except Exception as e:
        import traceback
        traceback.print_exc()
        return {
            "status": "error",
            "message": str(e)
        }

def split_into_characters(text, box, confidence, max_chars=500):
    """
    Split a text string into individual characters with estimated bounding boxes.
    
    Args:
        text (str): The text to split.
        box (list): Bounding box for the entire text as [[x1,y1],[x2,y2],[x3,y3],[x4,y4]].
        confidence (float): Confidence score for the text.
        max_chars (int): Maximum number of characters to process (default: 500).
    
    Returns:
        list: List of dictionary items for each character with its estimated position.
    """
    if not text or len(text) <= 1:
        return [{
            "rect": box,
            "text": text,
            "confidence": confidence,
            "is_character": True
        }]
    
    # Limit char for performance
    if len(text) > max_chars:
        text = text[:max_chars]
        print(f"Warning: Text truncated to {max_chars} characters to avoid performance issues")
    
    # Ensure box has 4 points
    if len(box) != 4:
        print(f"Warning: Box doesn't have 4 points: {box}. Creating simple rectangle.")
        # Create a simple rectangle if the box doesn't have 4 points
        box = [[0, 0], [100, 0], [100, 30], [0, 30]]
    
    # Extract coordinates from the box
    tl = box[0]  # top-left
    tr = box[1]  # top-right
    br = box[2]  # bottom-right
    bl = box[3]  # bottom-left
    
    text_len = len(text)
    x_increment_top = (tr[0] - tl[0]) / text_len
    x_increment_bottom = (br[0] - bl[0]) / text_len
    
    y_diff_top = tr[1] - tl[1]
    y_diff_bottom = br[1] - bl[1]
    
    start_x_top = tl[0]
    start_x_bottom = bl[0]
    
    # Create list result
    char_results = [{} for _ in range(text_len)]
    
    # Generate character boxes
    for i, char in enumerate(text):
        ratio1 = i / text_len
        ratio2 = (i + 1) / text_len
        
        x1_top = start_x_top + (i * x_increment_top)
        x2_top = start_x_top + ((i + 1) * x_increment_top)
        x1_bottom = start_x_bottom + (i * x_increment_bottom)
        x2_bottom = start_x_bottom + ((i + 1) * x_increment_bottom)
        
        y_top_left = tl[1] + (y_diff_top * ratio1)
        y_top_right = tl[1] + (y_diff_top * ratio2)
        y_bottom_left = bl[1] + (y_diff_bottom * ratio1)
        y_bottom_right = bl[1] + (y_diff_bottom * ratio2)
        
        # Create bounding box for character
        char_box = [
            [x1_top, y_top_left],       # top-left
            [x2_top, y_top_right],      # top-right
            [x2_bottom, y_bottom_right], # bottom-right
            [x1_bottom, y_bottom_left]   # bottom-left
        ]
        
        # Add to result
        char_results[i] = {
            "rect": char_box,
            "text": char,
            "confidence": confidence,
            "is_character": True
        }
    
    return char_results