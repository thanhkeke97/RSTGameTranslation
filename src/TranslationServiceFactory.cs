using System;

namespace WPFScreenCapture
{
    /// <summary>
    /// Factory class for creating translation service instances based on configuration
    /// </summary>
    public static class TranslationServiceFactory
    {
        /// <summary>
        /// Create a translation service instance based on the current configuration
        /// </summary>
        /// <returns>An implementation of ITranslationService</returns>
        public static ITranslationService CreateService()
        {
            string currentService = ConfigManager.Instance.GetCurrentTranslationService();
            
            return currentService switch
            {
                "Gemini" => new GeminiTranslationService(),
                "Ollama" => new OllamaTranslationService(),
                "ChatGPT" => new ChatGptTranslationService(),
                _ => new GeminiTranslationService() // Default to Gemini if unknown
            };
        }
        
        /// <summary>
        /// Create a specific translation service by name
        /// </summary>
        /// <param name="serviceName">Name of the service (Gemini, Ollama, or ChatGPT)</param>
        /// <returns>An implementation of ITranslationService</returns>
        public static ITranslationService CreateService(string serviceName)
        {
            return serviceName switch
            {
                "Gemini" => new GeminiTranslationService(),
                "Ollama" => new OllamaTranslationService(),
                "ChatGPT" => new ChatGptTranslationService(),
                _ => throw new ArgumentException($"Unknown translation service: {serviceName}")
            };
        }
    }
}