using System;
using System.Configuration;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Web.Services;
using System.Web.Script.Services;
using System.Web.UI;

namespace LLMStudioChat
{
    public partial class Default : Page
    {
        protected void Page_Load(object sender, EventArgs e) { }

        [WebMethod]
        [ScriptMethod(ResponseFormat = ResponseFormat.Json)]
        public static async Task<string> Ask(string message)
        {
            // Config
            var baseUrl = ConfigurationManager.AppSettings["LLM_Studio_BaseUrl"]?.TrimEnd('/');
            var apiKey = ConfigurationManager.AppSettings["LLM_Studio_ApiKey"];
            var model = ConfigurationManager.AppSettings["LLM_Studio_Model"] ?? "llama-3.1-8b-instruct";
            var system = ConfigurationManager.AppSettings["LLM_SystemPrompt"] ?? "Asistente.";
            var tempStr = ConfigurationManager.AppSettings["LLM_Temperature"] ?? "0.2";
            var maxTokensStr = ConfigurationManager.AppSettings["LLM_MaxTokens"] ?? "1024";

            if (string.IsNullOrWhiteSpace(baseUrl))
                return "Configuración inválida: LLM_Studio_BaseUrl no está definido.";

            if (string.IsNullOrWhiteSpace(message))
                return "Debes ingresar un mensaje.";

            if (!double.TryParse(tempStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double temperature))
                temperature = 0.2;
            if (!int.TryParse(maxTokensStr, out int maxTokens))
                maxTokens = 1024;

            var endpoint = $"{baseUrl}/chat/completions";

            var payload = new
            {
                model = model,
                messages = new object[]
                {
                    new { role = "system", content = system },
                    new { role = "user",   content = message }
                },
                temperature = temperature,
                max_tokens = maxTokens,
                stream = false
            };

            try
            {
                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(120);
                    http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                    // Muchos servidores LM Studio no exigen API key. Si tienes una, envíala como Bearer.
                    if (!string.IsNullOrWhiteSpace(apiKey))
                        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                    var json = Newtonsoft.Json.JsonConvert.SerializeObject(payload);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    var resp = await http.PostAsync(endpoint, content).ConfigureAwait(false);
                    var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (!resp.IsSuccessStatusCode)
                    {
                        // Devuelve el error del backend de forma legible
                        return $"LLM Studio devolvió {(int)resp.StatusCode} - {resp.ReasonPhrase}: {body}";
                    }

                    // Esquema OpenAI-compatible: choices[0].message.content
                    dynamic parsed = Newtonsoft.Json.JsonConvert.DeserializeObject(body);
                    var text = (string)(parsed?.choices?[0]?.message?.content ?? "");
                    if (string.IsNullOrWhiteSpace(text))
                        text = "(Sin contenido en la respuesta del modelo)";
                    return text.Trim();
                }
            }
            catch (Exception ex)
            {
                return "Excepción al consultar LLM Studio: " + ex.Message;
            }
        }
    }
}
