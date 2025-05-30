## RSTGameTranslation (Realtime Screen Translation)
This product includes software from developed by Seth A. Robinson [sethRobinson's UGTLive](https://github.com/SethRobinson/UGTLive) 

## [![Version](https://img.shields.io/badge/version-0.45-blue.svg)](https://github.com/thanhkeke97/RSTGameTranslation/releases)

For Vietnamese users, a user guide in Vietnamese language is available [here](https://thanhkeke97.github.io/RSTGameTranslation/)

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
* Your game need set to the windowed, fullscreen borderless or windowed borderless mode
* It works best when you have an NVIDIA GPU.
* You need add conda to your PATH environment variable and install conda with option "Just me"
* If you don't have a dedicated GPU or your GPU is not NVIDIA, it will use the CPU but will provide very poor performance (you will lose 30-50% of your CPU power to use it).
* You can use Windows OCR (Windows OCR will be very lightweight) instead of Easy OCR or Paddle OCR, but the recognition results in games are very poor. I suggest only using Windows OCR for tasks other than gaming.
* The smaller the translation area you select, the faster the translation speed, and vice versa.
* To use Easy OCR and Paddle OCR, you need to run the server first. Instructions are provided below.

## How to install and use it (Windows) ##

*Prerequisites:*

* Download the latest version (zip file) [here](https://github.com/thanhkeke97/RSTGameTranslation/releases) and unzip it somewhere

* Do you have Conda?  To check, open a command window (press `Win + R`, type `cmd`, and hit Enter) and type "conda". If it shows conda commands, you already have it installed. If it gives an error, follow the steps below to install Miniconda [(download)](https://repo.anaconda.com/miniconda/Miniconda3-py39_25.3.1-1-Windows-x86_64.exe)

* Note:  When running .exe files you might get an ugly "This is dangerous, don't run it" message because this project is open source and i don't have any digital signatures so you'll have to just trust me and click "More info" and run it anyway.  This message only happens the first time per .exe file.

*How to use it:*

* Run RSTGameTranslation/rst.exe to start the application
  
----- Setting ----------
* Go to setting on Language tab choose the language you want to translate from and to
* Go to setting on Translation tab choose the translation service you want to use
* Go to setting on OCR tab choose the OCR method you want to use
* Now you can close setting popup

----- Setup server (Only do it once for each OCR method) ------
* Click on SetupServer button to start setup server base on OCR method which you choose in the setting (If you choose Windows OCR, you can skip this step)
* Setup can take 5-15 minutes, depending on your internet speed and computer power
* Now wait for the server setup, when it finished, you will see a message "... environment setup completed"
  
----- Start translation ------
* Click on StartServer button and wait until you see a message "Successfully connected to .... server" (If you choose Windows OCR, you can skip this step)
* Now you can start translation by click on Start button
* You can drag the translation area to the area you want to translate
* You can change area to translate by click and drag on the translation area
* The translation result will be displayed in the chat window (button ChatBox) or in Monitor (button Monitor)
  
----- Setting LLMS ----------
* Go to the settings and add your Gemini API key.  There is some info written there on how to get it.

![alt text](media/settings_gemini.png)

* Check out the other settings; the defaults should be ok.  Notice that there is a place to enter the name of the game, this matters!  The LLM knowing this will help it correct errors and create better dialog, as it's more likely to know some weird word is the name of a character, etc.

* Now you should be ready.  Click Start and see what happens!  Click "Log" to see errors and things.  If stuff doesn't work or you have questions, try posting here on GitHub.

* You can use source_language and target_language in prompt, it will automatically map the language to the language code which you choose in the settings. For example, if you choose source_language=en and target_language=vi in the setting, the prompt sent to LLM will be convert to "English" and "Vietnamese" before sending to LLM automatically.

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
| Alt+G     | Start/Stop OCR (Can using when main application is not focused)|
| Shift+M   | Show/Hide Monitor Window |
| Shift+C   | Show/Hide ChatBox |
| Shift+P   | Show/Hide Settings |
| Shift+L   | Show/Hide Log Console |
| Alt+H     | Show/Hide Main Window (Can using when main application is not focused)|

## Advanced setup info ##
While personally I recommend Gemini Flash 2 lite and PaddleOCR (It is better for Asian languages and uses lower resources for easy OCR), there are a lot of options at your disposal. You can use Windows' built-in OCR instead of the python server, this doesn't work great for Japanese I've found, but might be ok for other languages.  It's fast.

Ollama and ChatGPT are other LLM options. For Ollama, install it, and set a model like gemma3:12b.  On an RTX 4090 it takes around 5 seconds to return a translation.  (the settings contain directions and buttons to get started)  For ChatGPT/OpenAI, choose a model like GPT-4.1 Nano.

Unfortunately I've hardcoded all the models so when new ones come out uh... well, maybe I'll move those to an editable file later.

All the OCR is done at a character-by-character level.  Then there is a "Block detection" function that sticks things together to make words and paragraphs.  You can edit the "Block Power" to make it more likely to stick things together or break them apart.  (Dialog is good stuck together, other things are better not stuck together, so depends on what you're doing)

## For developers - How to compile it ##

* Open the solution with Visual Studio 2022 and click compile.  I can't remember if it's going to automatically download the libraries it needs or not.

* For the python server, I use VSCode to write/debug it.  It's super simple, EasyOCR and PaddleOCR are doing the heavy lifting. 
