<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="Default.aspx.cs" Inherits="LLMStudioChat.Default" %>
<!DOCTYPE html>
<html lang="es">
<head runat="server">
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>Chat LLM Studio</title>

  <!-- Bootstrap 5 & Font Awesome (CDN) -->
  <link href="https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css" rel="stylesheet" />
  <link href="https://cdnjs.cloudflare.com/ajax/libs/font-awesome/6.5.2/css/all.min.css" rel="stylesheet" />

  <style>
    body { background:#f6f7fb; }
    .chat-card { max-width: 900px; margin: 24px auto; }
    .chat-log { height: 60vh; overflow-y: auto; background:#fff; border:1px solid #e9ecef; border-radius:.5rem; padding:1rem; }
    .msg { display:flex; gap:.75rem; margin-bottom:1rem; }
    .msg .avatar { width:36px; height:36px; display:flex; align-items:center; justify-content:center; border-radius:50%; background:#e9ecef; }
    .msg.user .bubble { background:#e7f1ff; align-self:flex-end; }
    .msg.assistant .bubble { background:#f1fff0; }
    .bubble { padding:.75rem 1rem; border-radius:.75rem; max-width:75%; }
    .msg.user { justify-content:flex-end; }
    .msg.user .avatar { order:2; background:#cfe2ff; }
    .msg.user .bubble { order:1; }
    .msg.assistant .avatar { background:#d1e7dd; }
    .footer-input { gap:.5rem; }
    .brand { font-weight:600; letter-spacing:.2px; }
  </style>
</head>
<body>
  <form id="form1" runat="server" class="container">
    <div class="card chat-card shadow-sm">
      <div class="card-header d-flex justify-content-between align-items-center">
        <div class="brand"><i class="fa-solid fa-robot me-2"></i>Chat LLM Studio</div>
        <small class="text-muted">v1.0.0</small>
      </div>

      <div id="chatLog" class="chat-log"></div>

      <div class="card-body">
        <div class="d-flex footer-input">
          <input id="txtMsg" type="text" class="form-control" placeholder="Escribe tu pregunta y presiona Enter..." />
          <button id="btnSend" type="button" class="btn btn-primary">
            <i class="fa-solid fa-paper-plane"></i>
          </button>
        </div>
        <div id="lblError" class="text-danger small mt-2 d-none"></div>
      </div>
    </div>
  </form>

  <script src="https://code.jquery.com/jquery-3.7.1.min.js"></script>
  <script src="https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/js/bootstrap.bundle.min.js"></script>
  <script>
      const $log = $("#chatLog");
      const $msg = $("#txtMsg");
      const $btn = $("#btnSend");
      const $err = $("#lblError");

      function addMessage(role, text) {
          const isUser = role === "user";
          const icon = isUser ? "fa-user" : "fa-robot";
          const cls = isUser ? "user" : "assistant";
          const $html = $(`
        <div class="msg ${cls}">
          ${isUser ? "" : `<div class="avatar"><i class="fa-solid ${icon}"></i></div>`}
          <div class="bubble">${$("<div>").text(text).html()}</div>
          ${isUser ? `<div class="avatar"><i class="fa-solid ${icon}"></i></div>` : ""}
        </div>`);
          $log.append($html);
          $log.scrollTop($log[0].scrollHeight);
      }

      function extractDetail(xhr) {
          try {
              const ct = xhr.getResponseHeader("Content-Type") || "";
              if (ct.indexOf("application/json") > -1 && xhr.responseText) {
                  const j = JSON.parse(xhr.responseText);
                  if (j && j.error && j.error.message) return `Detalle: ${j.error.message}`;
                  if (typeof j.d === "string") return j.d;
                  if (typeof j.message === "string") return j.message;
              }
          } catch { /* ignore */ }
          if (xhr.responseText) {
              const t = xhr.responseText.toString();
              return "Detalle: " + (t.length > 300 ? t.substring(0, 300) + " …" : t);
          }
          return "";
      }

      function explainAjaxError(xhr, textStatus, errorThrown) {
          if (!navigator.onLine) return "Sin conexión a Internet.";
          if (textStatus === "timeout") return "Tiempo de espera agotado al contactar el servidor.";
          if (!xhr) return "Error desconocido (sin detalles).";
          if (xhr.status === 0) return "No fue posible conectar con el servidor (fuera de línea o bloqueado).";
          if (xhr.status === 404) return "Servicio no encontrado (404). Verifica 'Default.aspx/Ask'.";
          if (xhr.status === 502) return "Error del modelo (502). " + extractDetail(xhr);
          if (xhr.status === 503) return "LLM Studio no está disponible (503). " + extractDetail(xhr);
          if (xhr.status === 504) return "Tiempo de espera con LLM Studio (504). " + extractDetail(xhr);
          return `Fallo de red: ${xhr.status} ${xhr.statusText || ""}. ${extractDetail(xhr)}`.trim();
      }

      function sendMessage() {
          $err.addClass("d-none").text("");
          const text = $msg.val().trim();
          if (!text) return;
          addMessage("user", text);
          $msg.val("");
          $btn.prop("disabled", true).html('<i class="fa-solid fa-circle-notch fa-spin"></i>');

          $.ajax({
              url: "Default.aspx/Ask",
              type: "POST",
              contentType: "application/json; charset=utf-8",
              dataType: "json",
              data: JSON.stringify({ message: text }),
              timeout: 30000,
              success: function (res) {
                  const reply = (res && typeof res.d === "string" && res.d.trim().length > 0)
                      ? res.d.trim()
                      : "LLM Studio no está disponible en este momento.";
                  addMessage("assistant", reply);
              },
              error: function (xhr, textStatus, errorThrown) {
                  const human = explainAjaxError(xhr, textStatus, errorThrown);
                  $err.removeClass("d-none").text(human);
                  addMessage("assistant", human);
              },
              complete: function () {
                  $btn.prop("disabled", false).html('<i class="fa-solid fa-paper-plane"></i>');
                  $msg.focus();
              }
          });
      }

      $btn.on("click", sendMessage);
      $msg.on("keydown", function (e) {
          if (e.key === "Enter") { e.preventDefault(); sendMessage(); }
      });
  </script>
</body>
</html>
