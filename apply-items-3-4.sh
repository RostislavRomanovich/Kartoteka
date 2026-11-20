#!/usr/bin/env bash
set -euo pipefail
set +H  # отключить history expansion (защита от '!')

if [ ! -d .git ]; then echo "ОШИБКА: запустите из корня git-репозитория"; exit 1; fi

echo "=== 0. Откат частичной индексации от неудачного запуска (файлы на диске НЕ трогаются) ==="
git reset --mixed HEAD >/dev/null 2>&1 || true
git status --short

echo "=== 1. Синхронизация с загруженными Migrations / installer/scripts ==="
branch=$(git rev-parse --abbrev-ref HEAD)
git fetch origin
git merge --ff-only "origin/$branch" || { echo "ОШИБКА: синхронизируйтесь вручную (git pull) и повторите"; exit 1; }

echo "=== 2. .gitignore (только если отсутствует) ==="
if [ ! -f .gitignore ]; then
cat > .gitignore <<'GITIGNORE_EOF'
# === Сборка .NET ===
[Bb]in/
[Oo]bj/
[Dd]ebug/
[Rr]elease/
x64/
x86/
artifacts/
TestResults/

# === Результаты Self-Contained публикации в корне ===
/*.dll
/*.exe
/*.pdb
/*.deps.json
/*.runtimeconfig.json
/*.staticwebassets.runtime.json
/*.staticwebassets.endpoints.json
/cs/
/de/
/es/
/fr/
/it/
/ja/
/ko/
/pl/
/pt-BR/
/ru/
/tr/
/zh-Hans/
/zh-Hant/
/runtimes/

# === Секреты и локальная конфигурация ===
/appsettings.json
/appsettings.Production.json
*.secrets.json
.env

# === IDE и ОС ===
.vs/
.vscode/
.idea/
*.user
*.suo
*.bak
Thumbs.db
Desktop.ini

# === Python ===
__pycache__/
*.pyc
.venv/

# === Установщик: тяжёлые payload-файлы ===
installer/payload/
*.log
GITIGNORE_EOF
echo ".gitignore создан."
else echo ".gitignore уже существует — пропускаю."; fi

echo "=== 3. wwwroot (только если отсутствует; существующий UI сохраняем) ==="
if [ ! -f wwwroot/index.html ]; then
mkdir -p wwwroot
cat > wwwroot/index.html <<'INDEX_HTML_EOF'
<!DOCTYPE html>
<html lang="ru">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <meta http-equiv="Content-Security-Policy"
          content="default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'">
    <title>Картотека</title>
    <style>
        * { box-sizing: border-box; }
        body { margin: 0; font-family: "Segoe UI", Arial, sans-serif; background: #f4f6f8; color: #1c2430; }
        .topbar { background: #1f3a5f; color: #fff; padding: 10px 20px; }
        .topbar h1 { margin: 0; font-size: 20px; }
        .card { background: #fff; margin: 16px; padding: 16px; border-radius: 8px; box-shadow: 0 1px 3px rgba(0,0,0,.15); }
        .toolbar { display: flex; gap: 12px; align-items: center; margin: 12px 16px; flex-wrap: wrap; }
        .toolbar h2 { margin: 0; flex: 1; }
        .row { display: flex; gap: 10px; align-items: center; flex-wrap: wrap; margin: 10px 0; }
        .muted { color: #5a6b7f; font-size: 14px; }
        .error { color: #b00020; min-height: 1em; }
        input, select, button { font: inherit; padding: 6px 10px; }
        button { cursor: pointer; }
        label { display: flex; flex-direction: column; gap: 4px; margin-bottom: 10px; }
        .row label { flex-direction: row; align-items: center; margin: 0; }
        .table-wrap { overflow: auto; max-height: 60vh; }
        table { border-collapse: collapse; width: 100%; }
        th, td { border-bottom: 1px solid #dde3ea; padding: 6px 10px; text-align: left; vertical-align: top; }
        th { background: #eef2f6; position: sticky; top: 0; }
        dialog { border: none; border-radius: 8px; max-width: 960px; width: 92vw; padding: 16px; }
        dialog::backdrop { background: rgba(0,0,0,.45); }
        dialog .toolbar { margin: 0 0 10px; }
        #login-view { max-width: 420px; margin: 40px auto; }
        #login-view button { width: 100%; }
    </style>
</head>
<body>
    <header class="topbar"><h1>Картотека</h1></header>

    <section id="login-view" class="card" hidden>
        <h2>Вход в систему</h2>
        <form id="login-form">
            <label>Электронная почта
                <input id="login-email" type="email" autocomplete="username" required>
            </label>
            <label>Пароль
                <input id="login-password" type="password" autocomplete="current-password" required>
            </label>
            <button id="login-button" type="submit">Войти</button>
            <p id="login-error" class="error" role="alert"></p>
        </form>
    </section>

    <section id="app-view" hidden>
        <div class="toolbar">
            <span id="current-user" class="muted"></span>
            <button id="audit-log-button" type="button" hidden>Журнал аудита</button>
            <button id="logout-button" type="button">Выйти</button>
        </div>

        <section class="card">
            <h2>Сотрудники</h2>
            <form id="search-form" class="row">
                <input id="employee-search" type="search" placeholder="Поиск: фамилия, имя, табельный номер">
                <button type="submit">Найти</button>
            </form>
            <div class="table-wrap">
                <table>
                    <thead>
                        <tr>
                            <th>Табельный №</th><th>ФИО</th><th>Дата рождения</th>
                            <th>Должность</th><th>Подразделение</th><th>Контракт до</th>
                        </tr>
                    </thead>
                    <tbody id="employees-tbody"></tbody>
                </table>
            </div>
            <div class="row">
                <button id="employees-prev" type="button">&#8592; Назад</button>
                <span id="employees-page-info" class="muted"></span>
                <button id="employees-next" type="button">Вперёд &#8594;</button>
                <span id="employees-status" class="muted" role="status"></span>
            </div>
        </section>
    </section>

    <dialog id="audit-log-dialog">
        <div class="toolbar">
            <h2>Журнал аудита</h2>
            <button id="audit-close-button" type="button">Закрыть</button>
        </div>
        <form id="audit-search-form" class="row">
            <input id="audit-search" type="search" placeholder="Поиск: пользователь, действие, объект">
            <button type="submit">Найти</button>
            <label>Записей на странице
                <select id="audit-page-size">
                    <option>25</option>
                    <option selected>50</option>
                    <option>100</option>
                    <option>200</option>
                </select>
            </label>
        </form>
        <div class="table-wrap">
            <table>
                <thead>
                    <tr>
                        <th>Время</th><th>Пользователь</th><th>Действие</th><th>Объект</th>
                        <th>ID объекта</th><th>IP</th><th>Детали</th>
                    </tr>
                </thead>
                <tbody id="audit-tbody"></tbody>
            </table>
        </div>
        <div class="row">
            <button id="audit-prev" type="button">&#8592; Назад</button>
            <span id="audit-page-info" class="muted"></span>
            <button id="audit-next" type="button">Вперёд &#8594;</button>
            <span id="audit-status" class="muted" role="status"></span>
        </div>
    </dialog>

    <noscript><p class="error">Для работы приложения требуется JavaScript.</p></noscript>
    <script src="/app.js"></script>
</body>
</html>
INDEX_HTML_EOF

cat > wwwroot/app.js <<'APP_JS_EOF'
'use strict';

/* Картотека — базовый клиент (этап 16). JWT в памяти вкладки; textContent; права проверяет сервер. */

const session = { accessToken: null, roles: [], email: null, expiresUtc: null, expiryTimer: null };
const employeesState = { page: 1, pageSize: 50, query: '' };
const auditState = { page: 1, pageSize: 50, total: 0, query: '' };

const $ = (id) => document.getElementById(id);
function setText(id, text) { $(id).textContent = text; }
function showView(name) {
    $('login-view').hidden = name !== 'login';
    $('app-view').hidden = name !== 'app';
}

function endSession(message) {
    session.accessToken = null; session.roles = []; session.email = null; session.expiresUtc = null;
    if (session.expiryTimer !== null) { clearTimeout(session.expiryTimer); session.expiryTimer = null; }
    const dialog = $('audit-log-dialog');
    if (dialog.open) dialog.close();
    $('audit-log-button').hidden = true;
    $('employees-tbody').replaceChildren();
    $('audit-tbody').replaceChildren();
    setText('current-user', '');
    showView('login');
    setText('login-error', message || '');
}

async function apiFetch(path, options = {}) {
    const headers = Object.assign({}, options.headers);
    if (session.accessToken) headers['Authorization'] = 'Bearer ' + session.accessToken;
    if (options.body !== undefined) headers['Content-Type'] = 'application/json';
    const response = await fetch(path, Object.assign({}, options, { headers: headers }));
    if (response.status === 401 && session.accessToken) {
        endSession('Сессия истекла или недействительна. Войдите снова.');
        throw new Error('Сессия истекла. Войдите снова.');
    }
    if (!response.ok) {
        let message = response.status === 403 ? 'Недостаточно прав для этого действия.'
            : 'Ошибка сервера (' + response.status + ').';
        try { const data = await response.json(); if (data && typeof data.error === 'string') message = data.error; }
        catch (e) { /* не JSON */ }
        throw new Error(message);
    }
    if (response.status === 204) return null;
    return response.json();
}

async function login(email, password) {
    const data = await apiFetch('/api/auth/login', { method: 'POST', body: JSON.stringify({ email: email, password: password }) });
    session.accessToken = data.accessToken;
    session.roles = Array.isArray(data.roles) ? data.roles : [];
    session.email = email;
    session.expiresUtc = data.expiresUtc;
    const expiryMs = Date.parse(session.expiresUtc);
    if (Number.isFinite(expiryMs)) {
        session.expiryTimer = setTimeout(() => endSession('Срок действия сессии истёк. Войдите снова.'), Math.max(0, expiryMs - Date.now()));
    }
    setText('current-user', session.email + ' (' + session.roles.join(', ') + ')');
    $('audit-log-button').hidden = !session.roles.includes('admin');
    showView('app');
    employeesState.page = 1;
    await loadEmployees();
}

function cell(value) {
    const td = document.createElement('td');
    td.textContent = value === null || value === undefined ? '' : String(value);
    return td;
}
function formatDate(value) {
    if (!value) return '';
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? String(value) : date.toLocaleDateString('ru-RU');
}

function renderEmployees(rows) {
    const tbody = $('employees-tbody');
    tbody.replaceChildren();
    for (const row of rows) {
        const tr = document.createElement('tr');
        tr.appendChild(cell(row.personnelNumber));
        tr.appendChild(cell([row.lastName, row.firstName, row.middleName].filter(Boolean).join(' ')));
        tr.appendChild(cell(formatDate(row.dateOfBirth)));
        tr.appendChild(cell(row.position));
        tr.appendChild(cell(row.organizationalUnitName));
        tr.appendChild(cell(formatDate(row.contractEnd)));
        tbody.appendChild(tr);
    }
}

async function loadEmployees() {
    setText('employees-status', 'Загрузка…');
    const params = new URLSearchParams({ page: String(employeesState.page), pageSize: String(employeesState.pageSize) });
    if (employeesState.query) params.set('q', employeesState.query);
    try {
        const rows = await apiFetch('/api/employees?' + params.toString());
        renderEmployees(rows);
        setText('employees-status', rows.length === 0 ? 'Ничего не найдено.' : 'Показано записей: ' + rows.length + '.');
        $('employees-prev').disabled = employeesState.page <= 1;
        $('employees-next').disabled = rows.length < employeesState.pageSize;
        setText('employees-page-info', 'Страница ' + employeesState.page);
    } catch (err) { setText('employees-status', err.message); }
}

function renderAudit(items) {
    const tbody = $('audit-tbody');
    tbody.replaceChildren();
    for (const item of items) {
        const tr = document.createElement('tr');
        tr.appendChild(cell(item.occurredUtc ? new Date(item.occurredUtc).toLocaleString('ru-RU') : ''));
        tr.appendChild(cell(item.actorEmail));
        tr.appendChild(cell(item.action));
        tr.appendChild(cell(item.entityType));
        tr.appendChild(cell(item.entityId));
        tr.appendChild(cell(item.ipAddress));
        tr.appendChild(cell(item.details));
        tbody.appendChild(tr);
    }
}

async function loadAuditLog(page = auditState.page, pageSize = auditState.pageSize) {
    auditState.page = Math.max(1, page);
    auditState.pageSize = Math.min(200, Math.max(1, pageSize));
    setText('audit-status', 'Загрузка…');
    const params = new URLSearchParams({ page: String(auditState.page), pageSize: String(auditState.pageSize) });
    if (auditState.query) params.set('q', auditState.query);
    try {
        const data = await apiFetch('/api/admin/audit?' + params.toString());
        auditState.total = data.total;
        renderAudit(data.items);
        const totalPages = Math.max(1, Math.ceil(data.total / auditState.pageSize));
        setText('audit-page-info', 'Страница ' + auditState.page + ' из ' + totalPages + ' (всего записей: ' + data.total + ')');
        $('audit-prev').disabled = auditState.page <= 1;
        $('audit-next').disabled = auditState.page >= totalPages;
        setText('audit-status', '');
    } catch (err) { setText('audit-status', err.message); }
}

document.addEventListener('DOMContentLoaded', () => {
    $('login-form').addEventListener('submit', async (event) => {
        event.preventDefault();
        setText('login-error', '');
        const button = $('login-button');
        button.disabled = true;
        try { await login($('login-email').value.trim(), $('login-password').value); $('login-password').value = ''; }
        catch (err) { setText('login-error', err.message); }
        finally { button.disabled = false; }
    });
    $('logout-button').addEventListener('click', () => endSession('Вы вышли из системы.'));
    $('search-form').addEventListener('submit', (event) => {
        event.preventDefault();
        employeesState.query = $('employee-search').value.trim();
        employeesState.page = 1;
        loadEmployees();
    });
    $('employees-prev').addEventListener('click', () => { if (employeesState.page > 1) { employeesState.page -= 1; loadEmployees(); } });
    $('employees-next').addEventListener('click', () => { employeesState.page += 1; loadEmployees(); });
    $('audit-log-button').addEventListener('click', () => { auditState.page = 1; $('audit-log-dialog').showModal(); loadAuditLog(1); });
    $('audit-close-button').addEventListener('click', () => $('audit-log-dialog').close());
    $('audit-search-form').addEventListener('submit', (event) => {
        event.preventDefault();
        auditState.query = $('audit-search').value.trim();
        loadAuditLog(1);
    });
    $('audit-prev').addEventListener('click', () => loadAuditLog(auditState.page - 1));
    $('audit-next').addEventListener('click', () => loadAuditLog(auditState.page + 1));
    $('audit-page-size').addEventListener('change', () => loadAuditLog(1, Number($('audit-page-size').value)));
    showView('login');
});
APP_JS_EOF
echo "wwwroot/index.html и wwwroot/app.js созданы."
else echo "wwwroot уже существует — не перезаписываю."; fi

echo "=== 4. Снять артефакты с контроля версий (файлы остаются на диске) ==="
git rm -r --cached --quiet --ignore-unmatch -- obj artifacts runtimes cs de es fr it ja ko pl pt-BR ru tr zh-Hans zh-Hant || true
git rm --cached --quiet --ignore-unmatch -- "*.dll" "*.exe" "*.pdb" "*.deps.json" "*.runtimeconfig.json" "*.staticwebassets.runtime.json" "*.staticwebassets.endpoints.json" || true

git add .gitignore wwwroot
echo "=== Изменения для коммита (проверьте, что исходники не затронуты) ==="
git status --short

if [ -n "$(git status --porcelain)" ]; then
    git commit -m "chore: .gitignore + очистка артефактов сборки; feat: базовый wwwroot (этап 16)"
    echo "Коммит создан. Отправьте: git push"
else
    echo "Нет изменений для коммита (возможно, всё уже применено)."
fi

echo "=== 5. Верификация: tests/check_stage16_audit.py ==="
if [ -f tests/check_stage16_audit.py ]; then
    set +e
    if command -v python3 >/dev/null 2>&1; then python3 tests/check_stage16_audit.py; else python tests/check_stage16_audit.py; fi
    rc=$?
    set -e
    if [ $rc -eq 0 ]; then echo "stage16: GREEN (9/9)." ; else echo "stage16: RED — см. вывод выше."; fi
else
    echo "Тест tests/check_stage16_audit.py не найден (проверьте синхронизацию)."
fi