## RSTGameTranslation (Realtime Screen Translation)
This product includes software from developed by Seth A. Robinson [sethRobinson's UGTLive](https://github.com/SethRobinson/UGTLive) 

RSTGameTranslation with the following features:

* Runs EasyOCR or PaddleOCR locally via a Python server to facilitate live automatic translations
* All translation is done with Large Language Models (LLMs) (Supports Gemini, ChatGPT, Ollama) and support google translate
* Can (optionally... it's slower) be run 100% locally by using Ollama with an LLM of your choice
* Powerful easy to use GUI; overlay a chat window where you want (good for lots of dialog like a visual novel) or translate the entire screen
* By sending information on the game being translated and previous context, translations can be more accurate than other methods
* Can speak sentences and create lesson plans

License:  BSD-style attribution, see [LICENSE.md](LICENSE.md)

*Things to know:*

 * This is experimental, mostly tested with Japanese to English translations and English to Vietnamese translations
 * Your privacy is important. The only web calls this app makes are to check this GitHub's media/latest_version_checker.json file to see if a new version is available. Be aware that if you use a cloud service for the translation (Gemini is recommended), they will see what you're translating. If you use Ollama, nothing at all is sent out.

## Important
* It will only work on Windows 10 and above.
* It works best when you have an NVIDIA GPU.
* If you don't have a dedicated GPU or your GPU is not NVIDIA, it will use the CPU but will provide very poor performance (you will lose 30-50% of your CPU power to use it).
* You can use Windows OCR (Windows OCR will be very lightweight) instead of Easy OCR or Paddle OCR, but the recognition results in games are very poor. I suggest only using Windows OCR for tasks other than gaming.
* The smaller the translation area you select, the faster the translation speed, and vice versa.
* To use Easy OCR and Paddle OCR, you need to run the server first. Instructions are provided below.

## OCR Methods

The application now supports new OCR engines:

###  PaddleOCR (New!)
- Faster processing speed compared to EasyOCR
- Superior accuracy, especially for Asian languages (Japanese, Chinese, Korean)
- Lower memory usage
- Better character-level recognition
- Enhanced image preprocessing capabilities
- Automatic image upscaling for low-resolution images
- Runs on port 9998

To use PaddleOCR instead of EasyOCR:
1. Run `app/webserver/RunServerPaddleOCR.bat` instead of the regular server
2. In the application settings, select PaddleOCR as the OCR method

## How to install and use it (Windows) ##

* Download the latest version (zip file) [here](https://github.com/thanhkeke97/RSTGameTranslation/releases) and unzip it somewhere

* Do you have Conda?  To check, open a command window (press `Win + R`, type `cmd`, and hit Enter) and type "conda". If it shows conda commands, you already have it installed. If it gives an error, follow the steps below to install Miniconda [(more info)](https://www.anaconda.com/docs/getting-started/miniconda/install#quickstart-install-instructions)

* Note:  When running .bat files you might get an ugly "This is dangerous, don't run it" message - the .exe itself is signed by RTsoft, but bat files don't have a way to be signed so you'll have to just trust me and click "More info" and run it anyway.  This message only happens the first time per .bat file.

* Conda is a thing that lets us install a bunch of python stuff without screwing up other python installs.  Let's do that now, double click *app/webserver/SetupServerCondaEnvNVidia.bat* and wait a long time while it installs a bunch of junk.  We need this for EasyOCR and PaddleOCR, the engines that we run locally to "look" at the screen.  Later, this server might also do more ML/AI work in future versions. (for example, doing subtitles of spoken dialog)

* Did that look like it installed ok?  It runs a self-test at the end.  If it did, you're now ready to run the server.

* Run *app/webserver/RunServerEasyOCR.bat* for EasyOCR or *app/webserver/RunServerPaddleOCR.bat* for PaddleOCR (recommended for better performance with Asian languages)

* Now run *app/rst.exe*

* Go to the settings and add your Gemini API key.  There is some info written there on how to get it.

![alt text](media/settings_gemini.png)

* Check out the other settings; the defaults should be ok.  Notice that there is a place to enter the name of the game, this matters!  The LLM knowing this will help it correct errors and create better dialog, as it's more likely to know some weird word is the name of a character, etc.

* Now you should be ready.  Click Start and see what happens!  Click "Log" to see errors and things.  If stuff doesn't work or you have questions, try posting here on GitHub.

NOTE: The first time you use EasyOCR or PaddleOCR with a new language, it has to download it first!  So it might seem broken, just wait a minute or two and start/stop application's translation and it should work.

## How to update your version
RSTGameTranslation will automatically check for updates when you start it. If a new version is available, you'll see a notification asking if you want to download it. To update:

* Download the latest version from the notification or from [here](https://github.com/thanhkeke97/RSTGameTranslation/releases)
* Close RSTGameTranslation if it's running
* Extract the new files over your existing installation
* That's it! Your settings and preferences will be preserved
* The update process is simple and safe - you won't lose any of your settings or customizations.

## Keyboard Shortcuts

The following shortcuts have been added to help you use the application more quickly and efficiently:

* Main Shortcuts:

| Shortcut  | Function  |
|-----------|-----------|
| Shift+S | Start/Stop OCR |
| Shift+M | Show/Hide Monitor Window |
| Shift+C | Show/Hide ChatBox |
| Shift+P | Show/Hide Settings |
| Shift+L | Show/Hide Log Console |
| Shift+H | Show/Hide Main Window |

## Advanced setup info ##
While personally I recommend Gemini Flash 2 and PaddleOCR (for Asian languages) or EasyOCR (for other languages), there are a lot of options at your disposal.  You can use Windows' built-in OCR instead of the python server, this doesn't work great for Japanese I've found, but might be ok for other languages.  It's fast.

Ollama and ChatGPT are other LLM options. For Ollama, install it, and set a model like gemma3:12b.  On an RTX 4090 it takes around 5 seconds to return a translation.  (the settings contain directions and buttons to get started)  For ChatGPT/OpenAI, choose a model like GPT-4.1 Nano.

Unfortunately I've hardcoded all the models so when new ones come out uh... well, maybe I'll move those to an editable file later.

All the OCR is done at a character-by-character level.  Then there is a "Block detection" function that sticks things together to make words and paragraphs.  You can edit the "Block Power" to make it more likely to stick things together or break them apart.  (Dialog is good stuck together, other things are better not stuck together, so depends on what you're doing)

## For developers - How to compile it ##

* Open the solution with Visual Studio 2022 and click compile.  I can't remember if it's going to automatically download the libraries it needs or not.

* For the python server, I use VSCode to write/debug it.  It's super simple, EasyOCR and PaddleOCR are doing the heavy lifting. 
