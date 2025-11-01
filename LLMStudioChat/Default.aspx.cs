using System;
using System.Configuration;
using System.Web;
using System.Web.Script.Services;
using System.Web.Services;
using System.Web.UI;
using Aurora.LLM;
using Newtonsoft.Json;

namespace LLMStudioChat
{
    public partial class Default : Page
    {
        protected void Page_Load(object sender, EventArgs e) { }

        [WebMethod]
        [ScriptMethod(ResponseFormat = ResponseFormat.Json)]
        public static string Ask(string message)
        {
            // Config desde web.config
            var providerStr = GetAppSetting("LLM_Provider") ?? "OpenAICompatible";
            var baseUrl = GetAppSetting("LLM_Studio_BaseUrl") ?? "http://localhost:1234";
            var apiKey = GetAppSetting("LLM_Studio_ApiKey");
            var model = GetAppSetting("LLM_Studio_Model") ?? "llama-3.1-8b-instruct";
            var apiVersion = GetAppSetting("LLM_Azure_ApiVersion") ?? "2024-02-15-preview";
            var system = GetAppSetting("LLM_SystemPrompt") ?? "Asistente técnico en español.";
            var tempStr = GetAppSetting("LLM_Temperature") ?? "0.2";
            var maxTokStr = GetAppSetting("LLM_MaxTokens") ?? "1024";
            var toStr = GetAppSetting("LLM_TimeoutSeconds") ?? "45";
            var retriesStr = GetAppSetting("LLM_MaxRetries") ?? "2";
            var failEmpty = (GetAppSetting("LLM_FailOnEmptyContent") ?? "true").Equals("true", StringComparison.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(message))
                return Fail("Debes ingresar un mensaje.", 400);

            LLMGeneric.LLMProvider provider;
            if (!Enum.TryParse(providerStr, true, out provider)) provider = LLMGeneric.LLMProvider.OpenAICompatible;

            double temperature;
            if (!double.TryParse(tempStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out temperature))
                temperature = 0.2;

            int maxTokens;
            if (!int.TryParse(maxTokStr, out maxTokens)) maxTokens = 1024;

            int timeoutSec;
            if (!int.TryParse(toStr, out timeoutSec)) timeoutSec = 45;

            int maxRetries;
            if (!int.TryParse(retriesStr, out maxRetries)) maxRetries = 2;

            var options = new LLMGeneric.LLMClientOptions
            {
                Provider = provider,
                ApiKey = apiKey,
                Model = model,               // En Azure: deploymentId
                BaseUrl = baseUrl,           // Ej: http://localhost:1234 (LM Studio)
                ApiVersion = apiVersion,     // Sólo Azure
                Timeout = TimeSpan.FromSeconds(timeoutSec),
                MaxRetries = maxRetries,
                FailOnEmptyContent = failEmpty
            };

            var client = new LLMGeneric(options);

            var req = new LLMGeneric.LLMRequest
            {
                Temperature = temperature,
                MaxTokens = maxTokens
            };
            // Inyecta system + user
            req.Messages.Add(new LLMGeneric.LLMMessage(LLMGeneric.LLMRole.System, system));
            req.Messages.Add(new LLMGeneric.LLMMessage(LLMGeneric.LLMRole.User, message));

            try
            {
                var task = client.GenerateChatAsync(req);
                task.Wait(); // WebMethod static: para simplificar sin async/await end-to-end
                var res = task.Result;

                if (res == null)
                    return Fail("Respuesta nula del cliente LLM.", 502);

                if (!res.IsSuccess)
                {
                    // Mapear a status adecuados
                    var status = res.Error != null && res.Error.StatusCode.HasValue ? res.Error.StatusCode.Value : 502;
                    var msg = res.Error != null ? res.Error.Message ?? "Error del modelo." : "Error del modelo.";
                    if (res.Error != null && !string.IsNullOrWhiteSpace(res.Error.RawBody))
                    {
                        // Adjunta un extracto
                        var raw = res.Error.RawBody;
                        if (raw.Length > 800) raw = raw.Substring(0, 800) + " …";
                        msg = $"{msg} | Detalle: {raw}";
                    }

                    // Heurística para timeouts/conexión caída
                    if (res.Error != null && string.Equals(res.Error.Code, "timeout", StringComparison.OrdinalIgnoreCase))
                        status = 504;
                    if (res.Error != null && string.Equals(res.Error.Code, "unhandled_exception", StringComparison.OrdinalIgnoreCase)
                        && (res.Error.RawBody ?? "").ToLowerInvariant().Contains("connection"))
                        status = 503;

                    return Fail(msg, status);
                }

                // OK
                var text = (res.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(text))
                    return Fail("LLM Studio respondió vacío o no interpretable.", 502);

                return text;
            }
            catch (AggregateException ae)
            {
                var ex = ae.Flatten().InnerException ?? ae;
                return MapExceptionToFail(ex);
            }
            catch (Exception ex)
            {
                return MapExceptionToFail(ex);
            }
        }

        // ---------------- Helpers ----------------

        private static string MapExceptionToFail(Exception ex)
        {
            var msg = (ex.Message ?? "").ToLowerInvariant();

            if (ex is System.Threading.Tasks.TaskCanceledException || msg.Contains("timed out") || msg.Contains("timeout"))
                return Fail("Tiempo de espera agotado al consultar LLM Studio.", 504);

            if (ex is System.Net.Http.HttpRequestException ||
                msg.Contains("connection refused") ||
                msg.Contains("no such host") ||
                msg.Contains("name or service not known") ||
                msg.Contains("unable to connect") ||
                (msg.Contains("connect") && msg.Contains("failed")))
                return Fail("No es posible conectar con LLM Studio (servidor fuera de línea o inaccesible).", 503);

            return Fail("Error al consultar LLM Studio: " + (ex.Message ?? ex.GetType().Name), 500);
        }

        /// <summary>
        /// Devuelve un mensaje y fija el StatusCode HTTP para que jQuery dispare 'error'.
        /// </summary>
        private static string Fail(string message, int statusCode)
        {
            var ctx = HttpContext.Current;
            if (ctx != null)
            {
                ctx.Response.StatusCode = statusCode;
                ctx.Response.TrySkipIisCustomErrors = true;
                ctx.Response.ContentType = "application/json; charset=utf-8";
                ctx.Response.Write(JsonConvert.SerializeObject(new { message }));
                ctx.Response.End();
            }
            return message;
        }

        private static string GetAppSetting(string key) => ConfigurationManager.AppSettings[key];
    }
}
