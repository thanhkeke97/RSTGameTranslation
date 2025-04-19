**Universal Game Translator Live**

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
 
*Setup instructions:*

* Download the latest version [here](https://www.rtsoft.com/files/UniversalGameTranslatorLive_Windows.zip) and unzip it somewhere
(todo)

License:  BSD style attribution, see [LICENSE.md](LICENSE.md)

**Credits and links**
- Written by Seth A. Robinson (seth@rtsoft.com) twitter: @rtsoft - [Codedojo](https://www.codedojo.com), Seth's blog
- [EasyOCR](https://github.com/JaidedAI/EasyOCR)
- [Claude code](https://docs.anthropic.com/en/docs/agents-and-tools/claude-code/overview)

