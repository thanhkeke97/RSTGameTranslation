## Universal Game Translator Live

[![Watch the video](media/5e565177-6ead-48b1-86c0-7dbdebe1f554.png)](https://www.youtube.com/watch?v=PFrWheMeT5k)

A complete rewrite of Universal Game Translator with the following features:

* Runs EasyOCR locally via a Python server to facilitate live automatic translations
* All translation is done with large language models (Supports Gemini, ChatGPT, Ollama)
* Can (optionally... it's slower) be run 100% locally by using Ollama with an LLM of your choice
* Powerful easy to use GUI, overlay a chat window where you want (good for lots of dialog like a visual novel) or translate the entire screen
* By sending information on the game being translated and previous context, translations can be more accurate than other methods
* Can speak sentences and create lesson plans

*Things to know:*

 * This is experimental, mostly tested with Japanese to English translations
 * Windows and NVidia card only (it will probably work with any card, but I don't know if EasyOCR will be hardware accelerated, would likely need to tweak the conda setup .bat)
 * Your privacy is important - the only web calls this app does is to check this github's media/latest_version_checker.json file to see if a new version is out.  Also beaware that if you use a cloud service for the translation (Gemini is recommended) they of course see what you're translating.  If you use Ollama, nothing at all is sent out.
 
## How to install and use it (Windows) ##

* Download the latest version [here](https://www.rtsoft.com/files/UniversalGameTranslatorLive_Windows.zip) and unzip it somewhere

* Do you have Conda?  If you open a command window and type "conda", it should show conda commands.  If it's an error, cut and paste this instead to install miniconda [(more info)](https://www.anaconda.com/docs/getting-started/miniconda/install#quickstart-install-instructions):
```
curl https://repo.anaconda.com/miniconda/Miniconda3-latest-Windows-x86_64.exe -o .\miniconda.exe
start /wait "" .\miniconda.exe /S
del .\miniconda.exe
```

* Conda is a thing that let's us install a bunch of python stuff without screwing up other python installs.  Let's do that now, double click *UGTLive/webserver/SetupServerCondaEnvNVidia.bat* and wait a long time while it installs a bunch of junk.  We need this for EasyOCR, the thing that we run locally to "look" at the screen.  Later, this server might also do more ML/AI work in future versions. (for example, doing subtitles of spoken dialog)

* Did that look like it installed ok?  It runs a self test at the end.  If it did, you're now ready to run the server.

* Run UGTLive/webserver/ RunServer.bat (Note: this "server" is only accessible locally by you, if you wanted it available beyond that, edit server.py, it has directions)

* Now run UGTLive/UGTLive.exe

* Go to the settings and add your Gemini API key.  There is some info written there on how to get it.

![alt text](media/settings_gemini.png)

* Now you should be ready.  Click Start and see what happens!  Click "Log" to see errors and things.  If stuff doesn't work or you have questions, try posting here on github.

License:  BSD style attribution, see [LICENSE.md](LICENSE.md)

**Credits and links**
- Written by Seth A. Robinson (seth@rtsoft.com) twitter: @rtsoft - [Codedojo](https://www.codedojo.com), Seth's blog
- [EasyOCR](https://github.com/JaidedAI/EasyOCR)
- [Claude code](https://docs.anthropic.com/en/docs/agents-and-tools/claude-code/overview)