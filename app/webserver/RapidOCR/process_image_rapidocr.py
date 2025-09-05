import os
import json
import time
import numpy as np
import tempfile
from PIL import Image, ImageEnhance, ImageFilter
from rapidocr import RapidOCR, OCRVersion, ModelType, LangDet, LangRec, EngineType
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
        RapidOCR: Initialized OCR engine
    """
    global OCR_ENGINE, CURRENT_LANG

    # Note: RapidOCR might handle languages differently than PaddleOCR

    # Map language codes to PaddleOCR language codes
    lang_map = {
        'ja': 'CH',
        'ko': 'LATIN',
        'en': 'LATIN',
        'ch_sim': 'CH',
        'fr': 'LATIN',
        'ru': 'LATIN',
        'de': 'LATIN',
        'es': 'LATIN',
        'it': 'LATIN',
        'hi': 'LATIN',
        'pt': 'LATIN',
        'ar': 'LATIN',
        'nl': 'LATIN',
        'pl': 'LATIN',
        'ro': 'LATIN',
        'fa': 'LATIN',
        'cs': 'LATIN',
        'id': 'LATIN',
        'th': 'LATIN',
        'ch_tra': 'CH'
    }

    # Use mapped language or default to input if not in map
    rapid_lang = lang_map.get(lang, lang)
    if rapid_lang == "LATIN":
        lang_ocr = LangRec.LATIN
    else:
        lang_ocr = LangRec.CH
    
    
    # Only reinitialize if language has changed or engine is not initialized
    if OCR_ENGINE is None or CURRENT_LANG != lang:
        print(f"Initializing RapidOCR engine with language: {lang}...")
        start_time = time.time()

        # Initialize RapidOCR
        # Note: RapidOCR may have different initialization parameters
        # Adjust as needed based on RapidOCR documentation
        OCR_ENGINE = RapidOCR(params={"EngineConfig.onnxruntime.use_dml": True,
                              "Det.ocr_version": OCRVersion.PPOCRV5,
                              "Rec.ocr_version": OCRVersion.PPOCRV5,
                              "Det.lang_type": LangDet.CH,
                              "Rec.lang_type": lang_ocr,
                              "Det.engine_type": EngineType.ONNXRUNTIME,
                              "Rec.engine_type": EngineType.ONNXRUNTIME,
                              "Det.model_type": ModelType.MOBILE,
                              "Rec.model_type": ModelType.MOBILE})
        CURRENT_LANG = lang
        
        initialization_time = time.time() - start_time
        print(f"RapidOCR initialization completed in {initialization_time:.2f} seconds")
        flag_file = os.path.join(tempfile.gettempdir(), "rapidocr_ready.txt")
        with open(flag_file, "w") as f:
            f.write("READY")
        print("Ready flag created!")
    else:
        print(f"Using existing RapidOCR engine with language: {lang}")

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
    
    # Convert back to RGB (OCR may expect an RGB image)
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

def process_image(image_path, lang='en', preprocess_images=True, upscale_if_needed=False, char_level="True"):
    """
    Process an image using RapidOCR and return the OCR results.
    
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
        # RapidOCR can take a file path directly
        result = ocr_engine(temp_image_path)
        print(f"OCR results received. Processing...")
        
        # Debug output to understand the result structure
        print(f"Result type: {type(result)}")
        
        # Remove temporary file if created
        if temp_image_path != image_path and os.path.exists(temp_image_path):
            os.remove(temp_image_path)
            print(f"Removed temporary file: {temp_image_path}")
        
        # Calculate processing time
        processing_time = time.time() - start_time
        
        # Prepare the results
        ocr_results = []
        
        # Process the result based on RapidOCR's output format
        try:
            # Based on the example output, RapidOCR returns a named tuple with:
            # boxes, txts, scores, word_results, elapse_list, elapse
            
            # Check if result contains the expected attributes
            if hasattr(result, 'boxes') and hasattr(result, 'txts') and hasattr(result, 'scores'):
                boxes = result.boxes
                texts = result.txts
                scores = result.scores
                
                print(f"Found {len(texts)} text items")
                
                # Process each text detection
                for i in range(len(texts)):
                    if i < len(boxes) and i < len(scores):
                        text = texts[i]
                        box = boxes[i]
                        confidence = float(scores[i]) if i < len(scores) else 0.0
                        
                        # Convert NumPy arrays to lists if needed
                        if hasattr(box, 'tolist'):
                            box = box.tolist()
                        
                        # Convert all coordinates to float for JSON serialization
                        try:
                            box_native = [[float(coord) for coord in point] for point in box]
                        except Exception as e:
                            print(f"Error converting box to box_native: {e}")
                            print(f"Box: {box}")
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
            
            # If result is a tuple or list with at least 3 elements (boxes, texts, scores)
            elif isinstance(result, (tuple, list)) and len(result) >= 3:
                boxes = result[0]
                texts = result[1]
                scores = result[2]
                
                print(f"Found {len(texts)} text items")
                
                # Process each text detection
                for i in range(len(texts)):
                    if i < len(boxes) and i < len(scores):
                        text = texts[i]
                        box = boxes[i]
                        confidence = float(scores[i]) if i < len(scores) else 0.0
                        
                        # Convert NumPy arrays to lists if needed
                        if hasattr(box, 'tolist'):
                            box = box.tolist()
                        
                        # Convert all coordinates to float for JSON serialization
                        try:
                            box_native = [[float(coord) for coord in point] for point in box]
                        except Exception as e:
                            print(f"Error converting box to box_native: {e}")
                            print(f"Box: {box}")
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
            
            # If result is a list of detection results (each with box, text, score)
            elif isinstance(result, list) and len(result) > 0 and isinstance(result[0], (list, tuple)) and len(result[0]) >= 2:
                for detection in result:
                    try:
                        # Each detection contains box and text with confidence
                        # Format might be: [box, text, confidence] or [box, [text, confidence]]
                        if len(detection) < 2:
                            print(f"Warning: Detection doesn't have enough elements: {detection}")
                            continue
                            
                        box = detection[0]
                        
                        # Handle different result formats
                        if isinstance(detection[1], (list, tuple)) and len(detection[1]) >= 2:
                            text = detection[1][0]
                            confidence = float(detection[1][1])
                        elif isinstance(detection[1], str):
                            text = detection[1]
                            confidence = float(detection[2]) if len(detection) > 2 else 0.0
                        else:
                            print(f"Warning: Unexpected result format: {detection}")
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
                        print(f"Error processing detection: {e}")
                        import traceback
                        traceback.print_exc()
                        continue
            
            # If we still didn't process any results, log an error
            if not ocr_results:
                print("Warning: No OCR results were processed. Result format may not be supported.")
                print(f"Result: {result}")
                
                # Try one more approach - directly access attributes if result is an object
                try:
                    if hasattr(result, '__dict__'):
                        result_dict = result.__dict__
                        print(f"Result attributes: {result_dict.keys()}")
                        
                        # Look for common attribute names
                        boxes_attr = next((attr for attr in ['boxes', 'box', 'bboxes', 'regions'] if attr in result_dict), None)
                        texts_attr = next((attr for attr in ['txts', 'texts', 'text', 'words'] if attr in result_dict), None)
                        scores_attr = next((attr for attr in ['scores', 'score', 'confidences', 'confidence'] if attr in result_dict), None)
                        
                        if boxes_attr and texts_attr:
                            boxes = getattr(result, boxes_attr)
                            texts = getattr(result, texts_attr)
                            scores = getattr(result, scores_attr) if scores_attr else [0.0] * len(texts)
                            
                            print(f"Found {len(texts)} text items using attribute access")
                            
                            # Process each text detection
                            for i in range(len(texts)):
                                if i < len(boxes) and i < len(scores):
                                    text = texts[i]
                                    box = boxes[i]
                                    confidence = float(scores[i]) if i < len(scores) else 0.0
                                    
                                    # Convert NumPy arrays to lists if needed
                                    if hasattr(box, 'tolist'):
                                        box = box.tolist()
                                    
                                    # Convert all coordinates to float for JSON serialization
                                    try:
                                        box_native = [[float(coord) for coord in point] for point in box]
                                    except Exception as e:
                                        print(f"Error converting box to box_native: {e}")
                                        print(f"Box: {box}")
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
                    print(f"Error trying to access result attributes: {e}")
        
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