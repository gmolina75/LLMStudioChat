using System;
using System.Configuration;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.Script.Services;
using System.Web.Services;
using System.Web.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LLMStudioChat
{
    public partial class Default : Page
    {
        private static readonly HttpClient _http;

        static Default()
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            _http.DefaultRequestHeaders.Accept.Clear();
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        protected void Page_Load(object sender, EventArgs e) { }

        [WebMethod]
        [ScriptMethod(ResponseFormat = ResponseFormat.Json)]
        public static async Task<string> Ask(string message)
        {
            // Config
            var baseUrl = GetAppSetting("LLM_Studio_BaseUrl")?.TrimEnd('/');
            var apiKey = GetAppSetting("LLM_Studio_ApiKey");
            var model = GetAppSetting("LLM_Studio_Model") ?? "llama-3.1-8b-instruct";
            var system = GetAppSetting("LLM_SystemPrompt") ?? "Asistente técnico en español.";
            var tempStr = GetAppSetting("LLM_Temperature") ?? "0.2";
            var maxTokStr = GetAppSetting("LLM_MaxTokens") ?? "1024";

            if (string.IsNullOrWhiteSpace(baseUrl))
                return Fail("Configuración inválida: LLM_Studio_BaseUrl no está definido.", 500);

            if (string.IsNullOrWhiteSpace(message))
                return Fail("Debes ingresar un mensaje.", 400);

            if (!double.TryParse(tempStr, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var temperature))
                temperature = 0.2;

            if (!int.TryParse(maxTokStr, out var maxTokens))
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
                var json = JsonConvert.SerializeObject(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using (var req = new HttpRequestMessage(HttpMethod.Post, endpoint))
                {
                    req.Content = content;
                    if (!string.IsNullOrWhiteSpace(apiKey))
                        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                    var resp = await _http.SendAsync(req).ConfigureAwait(false);
                    var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

                    JToken tok = null;
                    try { tok = JToken.Parse(body); } catch { /* no JSON */ }

                    if (!resp.IsSuccessStatusCode)
                    {
                        // Error del backend LLM → 502
                        var detail = ExtractErrorDetail(tok, body);
                        return Fail($"Error del modelo: {detail}", 502);
                    }

                    // 2xx → intenta leer contenido
                    var text =
                        tok?.SelectToken("choices[0].message.content")?.ToString()
                        ?? tok?.SelectToken("choices[0].text")?.ToString()
                        ?? tok?.SelectToken("response")?.ToString()
                        ?? tok?.SelectToken("output")?.ToString()
                        ?? string.Empty;

                    if (!string.IsNullOrWhiteSpace(text))
                        return text.Trim();

                    var finish = tok?.SelectToken("choices[0].finish_reason")?.ToString();
                    if (!string.IsNullOrWhiteSpace(finish))
                        return $"(Sin contenido; finish_reason = {finish})";

                    // Si llega aquí, respuesta vacía del modelo → 502
                    var snippet = string.IsNullOrWhiteSpace(body) ? "(vacío)" : Trunc(body);
                    return Fail($"Respuesta vacía/no interpretable del modelo. Cuerpo: {snippet}", 502);
                }
            }
            catch (Exception ex)
            {
                var msg = ex.Message ?? string.Empty;
                var lower = msg.ToLowerInvariant();

                // Timeouts → 504
                if (ex is TaskCanceledException || lower.Contains("timed out") || lower.Contains("timeout"))
                    return Fail("Tiempo de espera agotado al consultar LLM Studio.", 504);

                // Conexión rechazada / DNS / inaccesible → 503
                if (ex is HttpRequestException ||
                    lower.Contains("connection refused") ||
                    lower.Contains("no such host") ||
                    lower.Contains("name or service not known") ||
                    lower.Contains("unable to connect") ||
                    (lower.Contains("connect") && lower.Contains("failed")))
                    return Fail("No es posible conectar con LLM Studio (servidor fuera de línea o inaccesible).", 503);

                System.Diagnostics.Trace.WriteLine(
                    $"[LLMStudio][{DateTime.UtcNow:o}] EXCEPTION {ex.GetType().Name}: {ex.Message}");
                return Fail("Error al consultar LLM Studio: " + ex.Message, 500);
            }
        }

        // --- Helpers ----------------------------------------------------------

        private static string GetAppSetting(string key)
            => ConfigurationManager.AppSettings[key];

        private static string Trunc(string s, int max = 800)
            => string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s.Substring(0, max) + " …");

        private static string ExtractErrorDetail(JToken tok, string raw)
        {
            var errMsg = tok?.SelectToken("error.message")?.ToString();
            var errType = tok?.SelectToken("error.type")?.ToString();
            var errCode = tok?.SelectToken("error.code")?.ToString();
            if (string.IsNullOrWhiteSpace(errMsg))
                errMsg = tok?.SelectToken("message")?.ToString();

            var detail = errMsg;
            if (!string.IsNullOrWhiteSpace(errType)) detail = $"{detail} (tipo: {errType})";
            if (!string.IsNullOrWhiteSpace(errCode)) detail = $"{detail} (código: {errCode})";

            if (string.IsNullOrWhiteSpace(detail))
                detail = string.IsNullOrWhiteSpace(raw) ? "(sin detalle)" : Trunc(raw);

            return detail;
        }

        /// <summary>
        /// Devuelve un mensaje y fija el StatusCode HTTP para que jQuery dispare 'error'.
        /// OJO: WebMethod retorna string, pero aún así podemos forzar código HTTP.
        /// </summary>
        private static string Fail(string message, int statusCode)
        {
            var ctx = HttpContext.Current;
            if (ctx != null)
            {
                ctx.Response.StatusCode = statusCode;
                ctx.Response.TrySkipIisCustomErrors = true;
                // Content-Type JSON ayuda a que el front intente parsear
                ctx.Response.ContentType = "application/json; charset=utf-8";
                // Devolvemos un envoltorio simple por compatibilidad:
                // jQuery error() leerá responseText y extractDetail lo mostrará.
                ctx.Response.Write(JsonConvert.SerializeObject(new { message }));
                // IMPORTANTE: terminar la respuesta para impedir que ASP.NET agregue su propio envoltorio
                ctx.Response.End();
            }
            // Valor de retorno por contrato (no se usará si Response.End() ya cortó el pipeline)
            return message;
        }
    }
}
