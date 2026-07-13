using System.Net;
using System.Text;

namespace NoraPremium.Service;

internal static class AdminHtml
{
    public const string Css = """
        :root{color-scheme:dark;--bg:#070a0f;--surface:#10151d;--surface2:#151c26;--line:#293443;--text:#f4f7fb;--muted:#91a0b5;--orange:#ff9c26;--orange2:#ffc065;--green:#39d98a;--red:#f06464}*{box-sizing:border-box}body{min-height:100vh;margin:0;background:radial-gradient(circle at 20% 0,#17202c 0,transparent 34%),var(--bg);color:var(--text);font:14px Inter,Segoe UI,sans-serif}a{color:inherit}.shell{width:min(1180px,calc(100% - 36px));margin:0 auto;padding:28px 0 50px}.shell.generated-shell{width:min(880px,calc(100% - 36px));padding-top:44px}.top{display:flex;align-items:center;justify-content:space-between;gap:20px;margin-bottom:28px}.brand{display:flex;align-items:center;gap:13px}.mark{width:42px;height:42px;border-radius:14px;display:grid;place-items:center;background:#ff9c2618;border:1px solid #ff9c2655;color:var(--orange);font-weight:800}.brand h1{font-size:22px;margin:0}.brand p{margin:3px 0 0;color:var(--muted);font-size:12px}.actions{display:flex;gap:9px;align-items:center}.actions-after{margin-top:16px}.spacer{height:22px}.grid{display:grid;grid-template-columns:repeat(4,1fr);gap:12px;margin-bottom:18px}.metric,.panel{background:#10151de8;border:1px solid var(--line);border-radius:18px}.metric{padding:17px}.metric b{display:block;font-size:26px;margin-top:5px}.eyebrow{color:#c69761;text-transform:uppercase;letter-spacing:.15em;font-size:10px;font-weight:800}.panel{padding:20px;margin-top:14px}.panelhead{display:flex;align-items:flex-end;justify-content:space-between;gap:16px;margin-bottom:15px}.panel h2{font-size:18px;margin:0}.panel p{color:var(--muted);margin:5px 0 0}.formgrid{display:grid;grid-template-columns:120px 130px 1fr auto;gap:10px}.input{width:100%;height:42px;padding:0 12px;border-radius:12px;border:1px solid var(--line);background:#090d13;color:var(--text);outline:none}.input:focus{border-color:#ff9c2688;box-shadow:0 0 0 3px #ff9c2613}.btn{display:inline-grid;place-items:center;text-decoration:none;height:42px;border:0;border-radius:12px;padding:0 15px;font-weight:750;cursor:pointer;background:var(--orange);color:#171009}.btn:hover{background:var(--orange2)}.btn.secondary{background:#1b2430;color:var(--text);border:1px solid var(--line)}.btn.danger{background:#32191d;color:#ffb0b0;border:1px solid #6d2d35}.btn.small{height:30px;padding:0 10px;border-radius:9px;font-size:11px}table{width:100%;border-collapse:collapse}th{text-align:left;color:#9aa8ba;text-transform:uppercase;letter-spacing:.1em;font-size:9px;padding:10px 9px;border-bottom:1px solid var(--line)}td{padding:11px 9px;border-bottom:1px solid #202a37;vertical-align:middle}tr:last-child td{border-bottom:0}.mono{font-family:Cascadia Mono,Consolas,monospace;font-size:12px}.muted{color:var(--muted)}.pill{display:inline-flex;align-items:center;gap:6px;border:1px solid var(--line);background:#0b1017;border-radius:999px;padding:5px 9px;font-size:11px}.pill.ok{color:#76e9ad;border-color:#2b7251}.pill.bad{color:#ff9d9d;border-color:#74353b}.rowactions{display:flex;gap:6px}.notice{padding:12px 14px;border:1px solid #ff9c264d;background:#ff9c2610;border-radius:12px;color:#ffd19a;margin-bottom:14px}.login{min-height:100vh;display:grid;place-items:center;padding:24px}.loginbox{width:min(420px,100%);padding:28px;background:#10151df0;border:1px solid var(--line);border-radius:22px;box-shadow:0 28px 90px #0008}.loginbox h1{margin:0 0 8px}.loginbox p{color:var(--muted);margin:0 0 22px}.stack{display:grid;gap:11px}.result-panel{padding:18px}.code-list{display:grid;gap:10px}.code-row{display:grid;grid-template-columns:34px minmax(0,1fr) auto;align-items:center;gap:10px;padding:10px;border:1px solid var(--line);border-radius:13px;background:#080c11}.code-index{display:grid;place-items:center;width:30px;height:30px;border-radius:9px;background:#ff9c2614;color:#d7a364;font:700 10px Cascadia Mono,monospace}.code-value{width:100%;min-width:0;border:0;background:transparent;color:#ffd4a3;outline:none;font:650 13px Cascadia Mono,Consolas,monospace;letter-spacing:.02em}.copy-btn{min-width:112px}.result-foot{display:flex;align-items:center;justify-content:space-between;gap:16px;margin-top:16px}.one-time{color:var(--muted);font-size:12px}.footer{color:#66758a;font-size:11px;margin-top:24px;text-align:center}@media(max-width:850px){.grid{grid-template-columns:repeat(2,1fr)}.formgrid{grid-template-columns:1fr 1fr}.formgrid .wide{grid-column:1/-1}.tablewrap{overflow:auto}}@media(max-width:520px){.shell{width:min(100% - 20px,1180px)}.grid{grid-template-columns:1fr 1fr}.top{align-items:flex-start}.formgrid{grid-template-columns:1fr}.formgrid .wide{grid-column:auto}.code-row{grid-template-columns:30px minmax(0,1fr)}.copy-btn{grid-column:1/-1;width:100%}.result-foot{align-items:stretch;flex-direction:column-reverse}}
        """;

    public const string Js = """
        (() => {
          document.addEventListener('click', async (event) => {
            const button = event.target.closest('[data-copy-target]');
            if (!button) return;
            const input = document.getElementById(button.dataset.copyTarget);
            if (!input) return;
            try {
              await navigator.clipboard.writeText(input.value);
            } catch {
              input.focus();
              input.select();
              document.execCommand('copy');
            }
            const original = button.textContent;
            button.textContent = 'Скопировано';
            button.classList.add('copied');
            setTimeout(() => { button.textContent = original; button.classList.remove('copied'); }, 1400);
          });
        })();
        """;

    public static string Login(string? error, string csrf)
    {
        var alert = string.IsNullOrWhiteSpace(error) ? "" : $"<div class=\"notice\">{E(error)}</div>";
        return Document("Вход", $"""
            <main class="login"><section class="loginbox">
              <div class="brand"><div class="mark">N</div><div><h1>NORA Premium</h1><p>Закрытая панель лицензий</p></div></div>
              <div class="spacer"></div>{alert}
              <form method="post" action="/admin/login" class="stack">
                <input type="hidden" name="csrf" value="{E(csrf)}">
                <label><span class="eyebrow">Логин</span><input class="input" name="username" value="admin" autocomplete="username" required></label>
                <label><span class="eyebrow">Пароль</span><input class="input" type="password" name="password" autocomplete="current-password" required></label>
                <button class="btn" type="submit">Войти в панель</button>
              </form>
            </section></main>
            """);
    }

    public static string Dashboard(DashboardData data, string csrf, string? message = null)
    {
        var notice = string.IsNullOrWhiteSpace(message) ? "" : $"<div class=\"notice\">{E(message)}</div>";
        var licenses = new StringBuilder();
        foreach (var item in data.Licenses)
        {
            var active = item.RevokedAt is null && (item.ExpiresAt is null || DateTimeOffset.Parse(item.ExpiresAt) > DateTimeOffset.UtcNow);
            licenses.Append($"""
                <tr><td class="mono">•••• {E(item.CodeLast4)}</td><td>{E(item.Note.Length == 0 ? "—" : item.Note)}</td>
                <td>{item.ActiveDevices}/{item.MaxDevices}</td><td class="muted">{ShortDate(item.CreatedAt)}</td>
                <td><span class="pill {(active ? "ok" : "bad")}">{(active ? "Активен" : "Отозван")}</span></td>
                <td><form method="post" action="/admin/licenses/{item.Id}/toggle"><input type="hidden" name="csrf" value="{E(csrf)}"><input type="hidden" name="revoked" value="{(active ? "true" : "false")}"><button class="btn small {(active ? "danger" : "secondary")}" type="submit">{(active ? "Отозвать" : "Вернуть")}</button></form></td></tr>
                """);
        }
        if (data.Licenses.Count == 0)
            licenses.Append("<tr><td colspan=6 class=muted>Коды ещё не создавались.</td></tr>");

        var activations = new StringBuilder();
        foreach (var item in data.Activations)
        {
            var active = item.RevokedAt is null;
            activations.Append($"""
                <tr><td class="mono">•••• {E(item.CodeLast4)}</td><td class="mono">{E(item.InstallationLabel)}</td><td>{E(item.AppVersion)}</td>
                <td class="muted">{ShortDate(item.FirstSeenAt)}</td><td class="muted">{ShortDate(item.LastSeenAt)}</td>
                <td><span class="pill {(active ? "ok" : "bad")}">{(active ? "Устройство активно" : "Заблокировано")}</span></td>
                <td><form method="post" action="/admin/activations/{item.Id}/toggle"><input type="hidden" name="csrf" value="{E(csrf)}"><input type="hidden" name="revoked" value="{(active ? "true" : "false")}"><button class="btn small {(active ? "danger" : "secondary")}" type="submit">{(active ? "Заблокировать" : "Вернуть")}</button></form></td></tr>
                """);
        }
        if (data.Activations.Count == 0)
            activations.Append("<tr><td colspan=7 class=muted>Активаций пока нет.</td></tr>");

        var audit = new StringBuilder();
        foreach (var item in data.Audit)
            audit.Append($"<tr><td class=\"muted mono\">{ShortDate(item.CreatedAt)}</td><td>{E(item.Action)}</td><td class=\"muted\">{E(item.Detail)}</td><td class=\"mono muted\">{E(item.RemoteIp)}</td></tr>");

        return Document("Лицензии", $"""
            <main class="shell"><header class="top"><div class="brand"><div class="mark">N</div><div><h1>NORA Premium</h1><p>Visual entitlement control plane</p></div></div>
            <div class="actions"><span class="pill ok">● Сервис онлайн</span><form method="post" action="/admin/logout"><input type="hidden" name="csrf" value="{E(csrf)}"><button class="btn secondary" type="submit">Выйти</button></form></div></header>
            {notice}
            <section class="grid"><div class="metric"><span class="eyebrow">Всего лицензий</span><b>{data.TotalLicenses}</b></div><div class="metric"><span class="eyebrow">Активных</span><b>{data.ActiveLicenses}</b></div><div class="metric"><span class="eyebrow">Отозвано</span><b>{data.RevokedLicenses}</b></div><div class="metric"><span class="eyebrow">Устройств</span><b>{data.ActiveDevices}</b></div></section>
            <section class="panel"><div class="panelhead"><div><h2>Создать Premium-коды</h2><p>Полные значения будут показаны только один раз.</p></div></div>
              <form method="post" action="/admin/codes/generate" class="formgrid"><input type="hidden" name="csrf" value="{E(csrf)}">
                <label><span class="eyebrow">Количество</span><input class="input" type="number" name="count" min="1" max="100" value="1"></label>
                <label><span class="eyebrow">Устройств</span><input class="input" type="number" name="max_devices" min="1" max="20" value="3"></label>
                <label class="wide"><span class="eyebrow">Заметка</span><input class="input" name="note" maxlength="160" placeholder="Покупатель, заказ или комментарий"></label>
                <button class="btn" type="submit">Сгенерировать</button>
              </form>
            </section>
            <section class="panel"><div class="panelhead"><div><h2>Лицензии</h2><p>Коды, лимиты устройств и статус доступа.</p></div></div><div class="tablewrap"><table><thead><tr><th>Код</th><th>Заметка</th><th>Устройства</th><th>Создан</th><th>Статус</th><th></th></tr></thead><tbody>{licenses}</tbody></table></div></section>
            <section class="panel"><div class="panelhead"><div><h2>Пользователи и устройства</h2><p>Одна строка — одна установка NORA VPN.</p></div></div><div class="tablewrap"><table><thead><tr><th>Код</th><th>Установка</th><th>Версия</th><th>Первая</th><th>Последняя</th><th>Статус</th><th></th></tr></thead><tbody>{activations}</tbody></table></div></section>
            <section class="panel"><div class="panelhead"><div><h2>Безопасность</h2><p>Смена пароля администратора.</p></div></div><form method="post" action="/admin/password" class="formgrid"><input type="hidden" name="csrf" value="{E(csrf)}"><label><span class="eyebrow">Текущий пароль</span><input class="input" type="password" name="current_password" required></label><label><span class="eyebrow">Новый пароль</span><input class="input" type="password" name="new_password" minlength="14" required></label><label class="wide"><span class="eyebrow">Повтор</span><input class="input" type="password" name="confirm_password" minlength="14" required></label><button class="btn secondary" type="submit">Изменить</button></form></section>
            <section class="panel"><div class="panelhead"><div><h2>Аудит</h2><p>Последние административные и лицензионные события.</p></div></div><div class="tablewrap"><table><thead><tr><th>Время UTC</th><th>Событие</th><th>Детали</th><th>IP</th></tr></thead><tbody>{audit}</tbody></table></div></section>
            <div class="footer">Premium меняет только внешний вид NORA VPN и не влияет на VPN-функциональность.</div></main>
            """);
    }

    public static string Generated(IReadOnlyList<string> codes, string csrf)
    {
        _ = csrf;
        var rows = new StringBuilder();
        for (var index = 0; index < codes.Count; index++)
        {
            var id = $"premium-code-{index + 1}";
            rows.Append($"""
                <div class="code-row"><span class="code-index">{index + 1:00}</span>
                <input id="{id}" class="code-value" value="{E(codes[index])}" readonly aria-label="Premium-код {index + 1}">
                <button class="btn secondary copy-btn" type="button" data-copy-target="{id}">Копировать</button></div>
                """);
        }
        return Document("Новые коды", $"""
            <main class="shell generated-shell"><header class="top"><div class="brand"><div class="mark">N</div><div><h1>Коды созданы</h1><p>{codes.Count} шт. · сохраните до ухода со страницы</p></div></div>
            <a class="btn secondary" href="/admin">В панель</a></header>
            <section class="panel result-panel"><div class="notice">Это единственный показ полных значений. В базе остаются только защищённые отпечатки.</div>
            <div class="code-list">{rows}</div><div class="result-foot"><span class="one-time">Скопируйте коды и передайте их нужным пользователям.</span><a class="btn" href="/admin">Готово</a></div></section></main>
            """);
    }

    private static string Document(string title, string body) => $"""
        <!doctype html><html lang="ru"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>{E(title)} · NORA Premium</title><link rel="stylesheet" href="/admin/app.css"><script defer src="/admin/app.js"></script></head><body>{body}</body></html>
        """;

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? "");
    private static string ShortDate(string value) => DateTimeOffset.TryParse(value, out var date) ? date.UtcDateTime.ToString("yyyy-MM-dd HH:mm") : E(value);
}
