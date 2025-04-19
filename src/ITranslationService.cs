using System;
using System.Threading.Tasks;

namespace WPFScreenCapture
{
    /// <summary>
    /// Interface for translation services to provide a common API for all translation providers
    /// </summary>
    public interface ITranslationService
    {
        /// <summary>
        /// Translate text using the service's API
        /// </summary>
        /// <param name="jsonData">The JSON data to translate</param>
        /// <param name="prompt">The prompt to guide the translation</param>
        /// <returns>The translation result as a JSON string or null if translation failed</returns>
        Task<string?> TranslateAsync(string jsonData, string prompt);
    }
}