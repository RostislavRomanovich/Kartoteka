'use strict';
(() => {
  const $ = (id) => document.getElementById(id);
  const state = { token: null, roles: [], alerts: [], employees: [], units: [], employeeDetails: null, page: 1, pageSize: 50, query: '', loading: false, currentEmployeeId: null, savingEmployee: false, currentRelatedKind: null, currentRelatedId: null, savingRelated: false, attachmentTargets: {}, customFieldDefinitions: [], selectedEmployeeIds: new Set(), employeePhotoUrl: null, exportFields: [], exportPreview: { headers: [], rows: [], sortIndex: -1, descending: false }, auditPage: 1, auditPageSize: 50, auditTotal: 0, auditQuery: '' };
  const severityLabels = { warning: 'Скоро', due: 'Сегодня', overdue: 'Просрочено' };
  const typeLabels = { contract: 'Контракт', 'driving-license': 'Водительская категория', 'classness-review': 'Классность', birthday: 'День рождения', umo: 'УМО' };
  const attachmentGroups = [
    ['passport', 'Паспорт'], ['military_id', 'Военный билет'], ['access', 'Документы допуска'],
    ['contract', 'Контракт'], ['driving_license', 'Водительское удостоверение'],
    ['car_registration', 'Документы на автомобиль'], ['birth_certificate', 'Свидетельство о рождении'],
    ['award', 'Наградные документы'], ['personal_feature', 'Фотографии особых примет'],
    ['relative', 'Документы родственников'], ['other', 'Прочие документы']
  ];
  const loginView = $('login-view'), appView = $('app-view');

  function text(tag, className, value) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    node.textContent = value == null || value === '' ? '—' : String(value);
    return node;
  }
  const noCapitalizeFields = new Set(['personnelNumber','passportSeries','passportNumber','militaryIdNumber','messenger','contact','messengerContactsJson','key','email','password']);
  function capitalizeTextValue(field, value) {
    const source = String(value ?? '');
    if (noCapitalizeFields.has(String(field || '')) || !source.trim()) return source;
    return source.replace(/(^|[\s-])([a-zа-яё])/giu, (_, prefix, letter) => prefix + letter.toLocaleUpperCase('ru-RU'));
  }
  document.addEventListener('focusout', (event) => {
    const input = event.target;
    if (!(input instanceof HTMLInputElement || input instanceof HTMLTextAreaElement)) return;
    if (input.type !== 'text' && input.tagName !== 'TEXTAREA') return;
    if (input.dataset.noCapitalize === 'true' || input.dataset.date != null || input.type === 'search') return;
    input.value = capitalizeTextValue(input.name || input.dataset.customFieldId || '', input.value);
  });

  function dateRu(value) {
    if (!value) return '—';
    const parts = String(value).slice(0, 10).split('-');
    return parts.length === 3 ? `${parts[2]}.${parts[1]}.${parts[0]}` : String(value);
  }
  function fio(employee) { return [employee.lastName, employee.firstName, employee.middleName].filter(Boolean).join(' '); }
  function showStatus(message) {
    const box = $('status-message');
    box.textContent = message || '';
    box.classList.toggle('hidden', !message);
  }
  function setLoggedIn(roles) {
    loginView.classList.add('hidden'); appView.classList.remove('hidden'); $('logout-button').classList.remove('hidden');
    $('session-label').textContent = roles.length ? `Роль: ${roles.join(', ')}` : 'Вход выполнен';
    const canEdit = roles.includes('admin') || roles.includes('editor');
    $('create-employee-button').classList.toggle('hidden', !canEdit);
    $('manage-custom-fields-button').classList.toggle('hidden', !roles.includes('admin'));
    $('manage-units-button').classList.toggle('hidden', !roles.includes('admin'));
    $('audit-log-button').classList.toggle('hidden', !roles.includes('admin'));
    $('export-table-button').classList.remove('hidden');
  }
  function logout() {
    state.token = null; state.roles = []; state.alerts = []; state.employees = []; state.selectedEmployeeIds.clear(); state.page = 1;
    if (state.employeePhotoUrl) { URL.revokeObjectURL(state.employeePhotoUrl); state.employeePhotoUrl = null; } state.currentEmployeeId = null; state.savingEmployee = false;
    if ($('employee-form-dialog').open) $('employee-form-dialog').close();
    if ($('employee-dialog').open) $('employee-dialog').close();
    if ($('related-form-dialog').open) $('related-form-dialog').close();
    if ($('custom-fields-dialog').open) $('custom-fields-dialog').close();
    $('create-employee-button').classList.add('hidden'); $('manage-custom-fields-button').classList.add('hidden'); $('audit-log-button').classList.add('hidden'); $('manage-units-button').classList.add('hidden');
    $('employee-attachments').classList.add('hidden'); $('employee-attachments-list').replaceChildren();
    $('employee-photo-section').classList.add('hidden'); $('employee-photo-image').classList.add('hidden'); $('employee-photo-image').removeAttribute('src');
    loginView.classList.remove('hidden'); appView.classList.add('hidden'); $('logout-button').classList.add('hidden');
    $('session-label').textContent = 'Не выполнен вход'; $('password').value = ''; $('login-error').textContent = '';
  }
  async function api(path, options = {}) {
    const headers = new Headers(options.headers || {});
    headers.set('Accept', 'application/json');
    if (state.token) headers.set('Authorization', `Bearer ${state.token}`);
    if (options.body != null) headers.set('Content-Type', 'application/json');
    const response = await fetch(path, { ...options, headers, cache: 'no-store' });
    if (response.status === 401) { logout(); throw new Error('Сессия завершена. Войдите снова.'); }
    if (!response.ok) {
      let message = `Ошибка запроса (${response.status})`;
      try { const body = await response.json(); message = body.error || body.title || message; } catch (_) { /* keep generic message */ }
      throw new Error(message);
    }
    if (response.status === 204) return null;
    return response.json();
  }
  $('login-form').addEventListener('submit', async (event) => {
    event.preventDefault(); $('login-error').textContent = '';
    const submit = $('login-form').querySelector('button[type="submit"]'); submit.disabled = true;
    try {
      const result = await api('/api/auth/login', { method: 'POST', body: JSON.stringify({ email: $('email').value.trim(), password: $('password').value }) });
      state.token = result.accessToken; state.roles = Array.isArray(result.roles) ? result.roles : [];
      setLoggedIn(state.roles); await refreshAll();
    } catch (error) { $('login-error').textContent = error.message || 'Не удалось войти.'; }
    finally { submit.disabled = false; }
  });
  $('logout-button').addEventListener('click', logout);
  $('organizational-unit-select').addEventListener('change', () => renderSubdivisionOptions(''));
  $('refresh-button').addEventListener('click', () => refreshAll());
  $('search-form').addEventListener('submit', (event) => { event.preventDefault(); state.query = $('employee-search').value.trim(); state.page = 1; loadEmployees(); });
  $('clear-search').addEventListener('click', () => { $('employee-search').value = ''; state.query = ''; state.page = 1; loadEmployees(); });
  $('prev-page').addEventListener('click', () => { if (state.page > 1) { state.page -= 1; loadEmployees(); } });
  $('next-page').addEventListener('click', () => { if (state.employees.length === state.pageSize) { state.page += 1; loadEmployees(); } });
  $('select-page-employees').addEventListener('change', () => { for (const employee of state.employees) { if ($('select-page-employees').checked) state.selectedEmployeeIds.add(Number(employee.id)); else state.selectedEmployeeIds.delete(Number(employee.id)); } renderEmployees(); });
  $('export-table-button').addEventListener('click', openExportTableDialog);
  $('export-table-close').addEventListener('click', () => $('export-table-dialog').close());
  $('export-table-cancel').addEventListener('click', () => $('export-table-dialog').close());
  $('export-fields-all').addEventListener('click', () => setExportFieldChecks(true));
  $('export-fields-default').addEventListener('click', () => { const defaults = new Set(['personnelNumber','lastName','firstName','middleName','position','organizationalUnitName','rank','contractStart','contractEnd']); for (const input of $('export-fields-list').querySelectorAll('input[type=checkbox]')) input.checked = defaults.has(input.value); });
  $('export-presets').addEventListener('change', applyExportPreset);
  $('export-preset-save').addEventListener('click', saveExportPreset);
  $('export-table-run').addEventListener('click', runTableExport);
  $('export-table-preview').addEventListener('click', previewTable);
  $('export-preview-search').addEventListener('input', renderExportPreviewTable);
  $('upload-employee-photo').addEventListener('click', chooseEmployeePhoto);
  $('delete-employee-photo').addEventListener('click', deleteEmployeePhoto);
  $('close-dialog').addEventListener('click', () => $('employee-dialog').close());
  $('create-employee-button').addEventListener('click', () => openEmployeeForm(null));
  $('edit-employee-button').addEventListener('click', () => { const id = state.currentEmployeeId; if (id != null) openEmployeeForm(id); });
  $('cancel-employee-form').addEventListener('click', () => $('employee-form-dialog').close());
  $('cancel-employee-form-top').addEventListener('click', () => $('employee-form-dialog').close());
  $('employee-form-dialog').addEventListener('click', (event) => { if (event.target === $('employee-form-dialog')) $('employee-form-dialog').close(); });
  $('employee-form').addEventListener('submit', saveEmployeeForm);
  $('related-form').addEventListener('submit', saveRelatedForm);
  $('manage-custom-fields-button').addEventListener('click', async () => {
    if (!state.roles.includes('admin')) return;
    $('custom-fields-dialog').showModal();
    await refreshCustomFieldDefinitions();
  });
  $('custom-fields-close').addEventListener('click', () => $('custom-fields-dialog').close());
  $('manage-units-button').addEventListener('click', async () => {
    if (!state.roles.includes('admin')) return;
    $('unit-form-error').textContent = ''; $('unit-form-error').classList.add('hidden');
    $('units-dialog').showModal();
    try { await loadOrganizationalUnits(); renderUnitManagement(); }
    catch (error) { setUnitFormError(error.message || 'Не удалось загрузить справочник подразделений.'); }
  });
  $('units-close').addEventListener('click', () => $('units-dialog').close());
  $('unit-form').addEventListener('submit', createOrganizationalUnit);
  $('custom-field-definition-form').addEventListener('submit', createCustomFieldDefinition);
  $('audit-log-button').addEventListener('click', async () => {
    if (!state.roles.includes('admin')) return;
    state.auditPage = 1; state.auditQuery = ''; $('audit-search').value = '';
    $('audit-log-dialog').showModal(); await loadAuditLog();
  });
  $('audit-log-close').addEventListener('click', () => $('audit-log-dialog').close());
  $('audit-filter-form').addEventListener('submit', async (event) => { event.preventDefault(); state.auditQuery = $('audit-search').value.trim(); state.auditPage = 1; await loadAuditLog(); });
  $('audit-clear').addEventListener('click', async () => { $('audit-search').value = ''; state.auditQuery = ''; state.auditPage = 1; await loadAuditLog(); });
  $('audit-prev').addEventListener('click', async () => { if (state.auditPage > 1) { state.auditPage -= 1; await loadAuditLog(); } });
  $('audit-next').addEventListener('click', async () => { if (state.auditPage * state.auditPageSize < state.auditTotal) { state.auditPage += 1; await loadAuditLog(); } });

  $('related-form-close').addEventListener('click', () => $('related-form-dialog').close());
  $('related-form-cancel').addEventListener('click', () => $('related-form-dialog').close());
  $('related-form-dialog').addEventListener('click', (event) => { if (event.target === $('related-form-dialog')) $('related-form-dialog').close(); });
  for (const input of document.querySelectorAll('[data-date]')) {
    input.addEventListener('input', () => input.removeAttribute('aria-invalid'));
    input.addEventListener('blur', () => { if (input.value.trim()) input.value = normalizeDateInput(input.value); });
  }
  $('employee-dialog').addEventListener('click', (event) => { if (event.target === $('employee-dialog')) $('employee-dialog').close(); });

  async function loadAuditLog() {
    if (!state.roles.includes('admin')) return;
    const body = $('audit-log-body'); body.replaceChildren(text('tr', '', ''));
    const loading = body.firstChild; const cell = text('td', 'muted', 'Загрузка…'); cell.colSpan = 5; loading.append(cell);
    $('audit-log-error').classList.add('hidden');
    try {
      const params = new URLSearchParams({ page: String(state.auditPage), pageSize: String(state.auditPageSize) });
      if (state.auditQuery) params.set('q', state.auditQuery);
      const result = await api(`/api/admin/audit?${params.toString()}`);
      state.auditTotal = Number(result.total || 0); body.replaceChildren();
      if (!result.items?.length) { const row = text('tr', '', ''); const empty = text('td', 'muted', 'Записей не найдено.'); empty.colSpan = 5; row.append(empty); body.append(row); }
      for (const item of result.items || []) {
        const row = document.createElement('tr');
        row.append(text('td', '', item.occurredUtc ? new Date(item.occurredUtc).toLocaleString('ru-RU') : '—'));
        row.append(text('td', '', item.actorEmail || 'Системное действие'));
        row.append(text('td', '', item.action));
        row.append(text('td', '', [item.entityType, item.entityId].filter(Boolean).join(': ')));
        row.append(text('td', '', [item.details, item.ipAddress ? `IP: ${item.ipAddress}` : ''].filter(Boolean).join(' · ')));
        body.append(row);
      }
      $('audit-page-label').textContent = `Страница ${state.auditPage} · всего записей: ${state.auditTotal}`;
      $('audit-prev').disabled = state.auditPage <= 1;
      $('audit-next').disabled = state.auditPage * state.auditPageSize >= state.auditTotal;
    } catch (error) {
      body.replaceChildren(); const row = text('tr', '', ''); const cell = text('td', 'muted', 'Не удалось загрузить журнал.'); cell.colSpan = 5; row.append(cell); body.append(row);
      $('audit-log-error').textContent = error.message || 'Ошибка загрузки журнала.'; $('audit-log-error').classList.remove('hidden');
    }
  }

  async function refreshAll() {
    if (!state.token || state.loading) return;
    state.loading = true; showStatus('Обновление данных…');
    try {
      const results = await Promise.all([api('/api/alerts'), loadEmployees(false)]);
      state.alerts = Array.isArray(results[0]) ? results[0] : [];
      renderAlerts(); renderEmployees();
      $('last-updated').textContent = `Обновлено: ${new Date().toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' })}`;
      showStatus('');
    } catch (error) { showStatus(error.message || 'Не удалось обновить данные.'); }
    finally { state.loading = false; }
  }
  async function loadEmployees(render = true) {
    const params = new URLSearchParams({ page: String(state.page), pageSize: String(state.pageSize) });
    if (state.query) params.set('q', state.query);
    const result = await api(`/api/employees?${params.toString()}`);
    state.employees = Array.isArray(result) ? result : [];
    if (render) renderEmployees();
    return state.employees;
  }
  function alertCard(alert, compact = false) {
    const card = text('article', `alert-card severity-${alert.severity || 'warning'}`);
    const main = text('div', 'alert-main', '');
    const heading = text('div', 'alert-title', alert.title || typeLabels[alert.type] || 'Уведомление');
    const person = text('span', 'alert-person', alert.employeeName || 'Сотрудник');
    person.title = 'Открыть карточку сотрудника';
    person.tabIndex = 0; person.setAttribute('role', 'button');
    person.addEventListener('click', () => openEmployee(alert.employeeId));
    person.addEventListener('keydown', (event) => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); openEmployee(alert.employeeId); } });
    const message = text('p', 'alert-message', alert.message || 'Требуется проверить данные.');
    const meta = text('div', 'alert-meta', '');
    meta.append(text('span', 'severity', severityLabels[alert.severity] || 'Внимание'));
    meta.append(text('span', '', typeLabels[alert.type] || alert.type || 'Уведомление'));
    meta.append(text('span', '', `Срок: ${dateRu(alert.dueDate)}`));
    main.append(heading, person, message, meta);
    card.append(main);
    if (!compact) {
      const open = text('button', 'button button-secondary alert-open', 'Открыть карточку');
      open.type = 'button'; open.addEventListener('click', () => openEmployee(alert.employeeId)); card.append(open);
    }
    return card;
  }
  function renderAlerts() {
    const list = $('alerts-list'); list.replaceChildren();
    $('alert-count').textContent = String(state.alerts.length);
    $('alerts-empty').classList.toggle('hidden', state.alerts.length !== 0);
    const columns = state.alerts.length === 0 ? 3 : Math.max(3, Math.ceil(state.alerts.length / 3) * 3);
    list.style.gridTemplateColumns = `repeat(${columns}, minmax(0, 1fr))`;
    for (const alert of state.alerts) list.append(alertCard(alert));
  }
  function renderEmployees() {
    const body = $('employees-body'); body.replaceChildren();
    $('employee-count').textContent = state.query ? `Результаты поиска: ${state.employees.length}` : `На странице: ${state.employees.length}`;
    $('page-label').textContent = `Страница ${state.page}`;
    $('prev-page').disabled = state.page <= 1; $('next-page').disabled = state.employees.length < state.pageSize;
    $('selected-employee-count').textContent = `Выбрано: ${state.selectedEmployeeIds.size}`;
    $('export-selected-count').textContent = String(state.selectedEmployeeIds.size);
    const pageIds = state.employees.map(e => Number(e.id));
    $('select-page-employees').checked = pageIds.length > 0 && pageIds.every(id => state.selectedEmployeeIds.has(id));
    $('select-page-employees').indeterminate = pageIds.some(id => state.selectedEmployeeIds.has(id)) && ! $('select-page-employees').checked;
    if (!state.employees.length) { const row = text('tr', '', ''); row.append(text('td', 'muted', 'Сотрудники не найдены.')); row.firstChild.colSpan = 6; body.append(row); return; }
    const highlighted = new Set(state.alerts.filter(a => a.highlightEmployeeName).map(a => Number(a.employeeId)));
    for (const employee of state.employees) {
      const row = document.createElement('tr');
      if (highlighted.has(Number(employee.id))) row.classList.add('employee-row-highlight');
      const selectCell = document.createElement('td'); const checkbox = document.createElement('input'); checkbox.type = 'checkbox'; checkbox.checked = state.selectedEmployeeIds.has(Number(employee.id)); checkbox.setAttribute('aria-label', `Выбрать ${fio(employee)}`);
      checkbox.addEventListener('change', () => { if (checkbox.checked) state.selectedEmployeeIds.add(Number(employee.id)); else state.selectedEmployeeIds.delete(Number(employee.id)); renderEmployees(); }); selectCell.append(checkbox); row.append(selectCell);
      const nameCell = document.createElement('td');
      const nameButton = text('button', `employee-name-link${highlighted.has(Number(employee.id)) ? ' highlight' : ''}`, fio(employee));
      nameButton.type = 'button'; nameButton.addEventListener('click', () => openEmployee(employee.id)); nameCell.append(nameButton);
      row.append(nameCell, text('td', '', employee.personnelNumber), text('td', '', employee.position), text('td', '', employee.organizationalUnitName || employee.subdivision));
      const actions = text('td', 'row-actions', ''); const open = text('button', 'button button-quiet', 'Карточка'); open.type = 'button'; open.addEventListener('click', () => openEmployee(employee.id)); actions.append(open); row.append(actions); body.append(row);
    }
  }
  async function openEmployee(id) {
    try {
      const employee = await api(`/api/employees/${encodeURIComponent(id)}`);
      state.currentEmployeeId = employee.id; state.attachmentTargets = {}; state.employeeDetails = null;
      $('edit-employee-button').classList.toggle('hidden', !(state.roles.includes('admin') || state.roles.includes('editor')));
      $('dialog-name').textContent = fio(employee);
      const fields = [
        ['Табельный номер', employee.personnelNumber], ['Дата рождения', dateRu(employee.dateOfBirth)], ['Серия паспорта', employee.passportSeries], ['Номер паспорта', employee.passportNumber], ['Кем выдан паспорт', employee.passportIssuedBy], ['Дата выдачи паспорта', dateRu(employee.passportIssuedOn)], ['Должность', employee.position], ['Подразделение', employee.organizationalUnitName || employee.subdivision], ['Звание', employee.rank], ['Семейное положение', employee.maritalStatus], ['Форма допуска', employee.accessForm], ['Дата оформления допуска', dateRu(employee.accessGrantedOn)], ['Кем оформлен допуск', employee.accessIssuedBy], ['Начало контракта', dateRu(employee.contractStart)], ['Срок контракта, месяцев', employee.contractTermMonths], ['Окончание контракта', dateRu(employee.contractEnd)], ['Дата перевода', dateRu(employee.transferDate)], ['Откуда переведён', employee.transferFrom], ['Военный билет', employee.militaryIdNumber], ['Военкомат', employee.militaryRegistrationOffice], ['Категория воинского учёта', employee.militaryRegistrationCategory], ['Статус воинского учёта', employee.militaryRegistrationStatus], ['Обновлено', employee.updatedUtc ? new Date(employee.updatedUtc).toLocaleString('ru-RU') : '—']
      ];
      const details = $('employee-details'); details.replaceChildren();
      for (const [label, value] of fields) { const item = text('div', 'detail-item', ''); item.append(text('div', 'detail-label', label), text('div', 'detail-value', value)); details.append(item); }
      const related = state.alerts.filter(a => Number(a.employeeId) === Number(employee.id));
      const alertList = $('employee-alerts'); alertList.replaceChildren();
      $('employee-alerts-empty').classList.toggle('hidden', related.length !== 0);
      for (const alert of related) alertList.append(alertCard(alert, true));
      $('employee-dialog').showModal();
      await loadEmployeeDetails(employee.id);
      await Promise.all([loadEmployeeAttachments(employee.id), loadEmployeePhoto(employee.id)]);
    } catch (error) { showStatus(error.message || 'Не удалось открыть карточку.'); }
  }

  async function refreshCustomFieldDefinitions() {
    const root = $('custom-field-definitions-list');
    root.replaceChildren();
    const scopes = ['employee', 'relative'];
    const definitions = [];
    for (const scope of scopes) {
      const items = await api(`/api/custom-fields?scope=${scope}`);
      definitions.push(...items);
    }
    state.customFieldDefinitions = definitions;
    if (!definitions.length) { root.append(text('p', 'muted small', 'Дополнительные поля ещё не созданы.')); return; }
    for (const item of definitions) {
      const row = text('div', 'related-card', '');
      const header = text('div', 'related-card-heading', '');
      header.append(text('strong', '', `${item.label} (${item.scope === 'employee' ? 'сотрудник' : 'родственник'})`));
      const deactivate = text('button', 'button button-quiet danger-button', 'Деактивировать');
      deactivate.type = 'button';
      deactivate.addEventListener('click', async () => {
        if (!window.confirm(`Скрыть поле «${item.label}»? Ранее введённые значения сохранятся.`)) return;
        try { await api(`/api/custom-fields/${item.id}`, { method: 'DELETE' }); await refreshCustomFieldDefinitions(); }
        catch (error) { $('custom-field-definition-error').textContent = error.message; $('custom-field-definition-error').classList.remove('hidden'); }
      });
      header.append(deactivate);
      row.append(header, text('p', 'muted small', `${item.dataType === 'boolean' ? 'Да / нет' : item.dataType === 'number' ? 'Число' : item.dataType === 'date' ? 'Дата' : 'Текст'} · ${item.isRequired ? 'Обязательное поле' : 'Необязательное поле'}`));
      root.append(row);
    }
  }
  function customFieldKeyFromLabel(label, scope) {
    const translit = { а:'a',б:'b',в:'v',г:'g',д:'d',е:'e',ё:'yo',ж:'zh',з:'z',и:'i',й:'y',к:'k',л:'l',м:'m',н:'n',о:'o',п:'p',р:'r',с:'s',т:'t',у:'u',ф:'f',х:'h',ц:'ts',ч:'ch',ш:'sh',щ:'shch',ъ:'',ы:'y',ь:'',э:'e',ю:'yu',я:'ya' };
    let base = [...String(label || '').toLocaleLowerCase('ru-RU')].map(ch => translit[ch] ?? ch).join('').replace(/[^a-z0-9]+/g, '_').replace(/^_+|_+$/g, '').slice(0, 80).replace(/_+$/g, '');
    if (!base) base = 'custom_field';
    if (!/^[a-z]/.test(base)) base = `field_${base}`;
    const existing = new Set(state.customFieldDefinitions.filter(item => item.scope === scope).map(item => String(item.key || '').toLowerCase()));
    let candidate = base, suffix = 2;
    while (existing.has(candidate.toLowerCase())) candidate = `${base}_${suffix++}`;
    return candidate;
  }
  async function createCustomFieldDefinition(event) {
    event.preventDefault();
    const form = $('custom-field-definition-form');
    const errorBox = $('custom-field-definition-error');
    errorBox.textContent = ''; errorBox.classList.add('hidden');
    const data = new FormData(form);
    const label = capitalizeTextValue('label', String(data.get('label') || '').trim());
    if (!label) { errorBox.textContent = 'Введите название поля.'; errorBox.classList.remove('hidden'); return; }
    const scope = String(data.get('scope') || 'employee');
    const payload = {
      scope, key: customFieldKeyFromLabel(label, scope), label, dataType: data.get('dataType'),
      isRequired: form.elements.namedItem('isRequired').checked,
      sortOrder: 0
    };
    try {
      await api('/api/custom-fields', { method: 'POST', body: JSON.stringify(payload) });
      form.reset();
      await refreshCustomFieldDefinitions();
      showStatus(`Дополнительное поле «${label}» создано.`);
    } catch (error) { errorBox.textContent = error.message || 'Не удалось создать поле.'; errorBox.classList.remove('hidden'); }
  }
  async function loadCustomFieldEditor(employeeId) {
    const root = $('custom-fields-editor'); root.replaceChildren();
    const definitions = await api('/api/custom-fields?scope=employee');
    let values = [];
    if (employeeId != null) values = await api(`/api/employees/${encodeURIComponent(employeeId)}/custom-fields`);
    const valueMap = new Map(values.map(v => [Number(v.id), v.value]));
    for (const def of definitions) {
      const label = text('label', '', def.label + (def.isRequired ? ' *' : ''));
      let input;
      if (def.dataType === 'boolean') {
        input = document.createElement('input'); input.type = 'checkbox'; input.checked = String(valueMap.get(Number(def.id)) || '').toLowerCase() === 'true';
      } else {
        input = document.createElement('input');
        input.type = def.dataType === 'number' ? 'number' : 'text';
        if (def.dataType === 'date') { input.inputMode = 'numeric'; input.placeholder = 'ДД.ММ.ГГГГ'; input.maxLength = 10; input.dataset.date = 'true'; }
        input.value = def.dataType === 'date' ? isoToRu(valueMap.get(Number(def.id)) || '') : (valueMap.get(Number(def.id)) || '');
      }
      input.dataset.customFieldId = String(def.id);
      if (def.isRequired) input.required = true;
      if (def.dataType === 'boolean') label.classList.add('checkbox-label');
      label.append(input); root.append(label);
    }
    $('custom-fields-editor-section').classList.toggle('hidden', definitions.length === 0);
  }

  function setUnitFormError(message) {
    const box = $('unit-form-error'); box.textContent = message || ''; box.classList.toggle('hidden', !message);
  }
  function renderUnitManagement() {
    const select = $('unit-parent-select');
    const previous = select.value;
    select.replaceChildren(new Option('Без родительского подразделения', ''));
    const list = $('units-list'); list.replaceChildren();
    const byId = new Map(state.units.map(unit => [Number(unit.id), unit]));
    const pathFor = (unit) => {
      const parts = [unit.name]; let parentId = unit.parentUnitId; const visited = new Set([Number(unit.id)]);
      while (parentId != null && byId.has(Number(parentId)) && !visited.has(Number(parentId))) {
        const parent = byId.get(Number(parentId)); visited.add(Number(parentId)); parts.unshift(parent.name); parentId = parent.parentUnitId;
      }
      return parts.join(' / ');
    };
    const sorted = [...state.units].sort((a,b) => pathFor(a).localeCompare(pathFor(b), 'ru'));
    for (const unit of sorted) {
      select.add(new Option(pathFor(unit), String(unit.id)));
      const item = text('div', 'unit-list-item', pathFor(unit)); list.append(item);
    }
    if ([...select.options].some(option => option.value === previous)) select.value = previous;
    if (!state.units.length) list.append(text('p', 'muted small', 'Подразделения ещё не созданы.'));
  }
  async function createOrganizationalUnit(event) {
    event.preventDefault();
    if (!state.roles.includes('admin')) { setUnitFormError('Создавать подразделения может только администратор.'); return; }
    const form = $('unit-form'); const nameInput = form.elements.namedItem('name');
    const name = capitalizeTextValue('name', nameInput.value.trim());
    const parentValue = form.elements.namedItem('parentUnitId').value;
    setUnitFormError('');
    if (!name) { setUnitFormError('Введите название подразделения.'); nameInput.focus(); return; }
    const save = $('unit-form-save'); save.disabled = true;
    try {
      await api('/api/organizational-units', { method: 'POST', body: JSON.stringify({ name, parentUnitId: parentValue ? Number(parentValue) : null }) });
      form.reset();
      await loadOrganizationalUnits(); renderUnitManagement();
      showStatus('Подразделение создано. Теперь его можно выбрать в карточке сотрудника.');
    } catch (error) { setUnitFormError(error.message || 'Не удалось создать подразделение.'); }
    finally { save.disabled = false; }
  }

  function unitById(id) { return state.units.find(unit => Number(unit.id) === Number(id)) || null; }
  function rootUnitId(id) {
    let unit = unitById(id); if (!unit) return null;
    const seen = new Set();
    while (unit.parentUnitId != null) {
      if (seen.has(Number(unit.id))) break;
      seen.add(Number(unit.id));
      const parent = unitById(unit.parentUnitId); if (!parent) break; unit = parent;
    }
    return Number(unit.id);
  }
  function relativeUnitPath(id, rootId) {
    let unit = unitById(id); if (!unit) return '';
    const parts = []; const seen = new Set();
    while (unit && Number(unit.id) !== Number(rootId) && !seen.has(Number(unit.id))) {
      seen.add(Number(unit.id)); parts.unshift(unit.name);
      unit = unit.parentUnitId == null ? null : unitById(unit.parentUnitId);
    }
    return parts.join(' / ');
  }
  function renderSubdivisionOptions(selectedValue = '') {
    const hidden = $('subdivision-select');
    const optionsRoot = $('subdivision-options');
    const summary = $('subdivision-selected-summary');
    const parentId = $('organizational-unit-select').value;
    optionsRoot.replaceChildren();
    const splitSelected = (value) => String(value || '').split(/\s+\|\s+/).map(item => item.trim()).filter(Boolean);
    const selected = new Set(splitSelected(selectedValue));
    const paths = [];
    if (parentId) {
      const byParent = new Map();
      for (const unit of state.units) {
        const key = unit.parentUnitId == null ? 'root' : String(unit.parentUnitId);
        if (!byParent.has(key)) byParent.set(key, []);
        byParent.get(key).push(unit);
      }
      const appendChildren = (parentKey, prefix, visited) => {
        const children = [...(byParent.get(parentKey) || [])].sort((a,b) => a.name.localeCompare(b.name, 'ru'));
        for (const unit of children) {
          if (visited.has(Number(unit.id))) continue;
          const nextVisited = new Set(visited); nextVisited.add(Number(unit.id));
          const path = prefix ? `${prefix} / ${unit.name}` : unit.name;
          paths.push({ path, depth: prefix ? prefix.split(' / ').length : 0, hasChildren: (byParent.get(String(unit.id)) || []).length > 0 });
          appendChildren(String(unit.id), path, nextVisited);
        }
      };
      appendChildren(String(parentId), '', new Set([Number(parentId)]));
    }
    if (!parentId) {
      optionsRoot.append(text('span', 'muted small', 'Сначала выберите подразделение по справочнику.'));
    } else if (!paths.length) {
      optionsRoot.append(text('span', 'muted small', 'Для выбранного подразделения нет дочерних уровней в справочнике.'));
    } else {
      for (const item of paths) {
        const row = text('label', `subdivision-option${item.depth ? ' subdivision-depth-' + Math.min(item.depth, 6) : ''}`, '');
        const checkbox = document.createElement('input');
        checkbox.type = 'checkbox'; checkbox.value = item.path; checkbox.checked = selected.has(item.path);
        checkbox.addEventListener('change', () => {
          const chosen = [...optionsRoot.querySelectorAll('input[type="checkbox"]:checked')].map(input => input.value);
          hidden.value = chosen.join(' | ');
          summary.textContent = chosen.length ? `Выбрано: ${chosen.join('; ')}` : 'Ничего не выбрано.';
        });
        const marker = text('span', 'subdivision-option-marker', item.depth ? '↳' : '•');
        const labelText = text('span', 'subdivision-option-path', `${item.path}${item.hasChildren ? '  ›' : ''}`);
        row.append(checkbox, marker, labelText);
        optionsRoot.append(row);
      }
      const unmatched = [...selected].filter(value => !paths.some(item => item.path === value));
      for (const value of unmatched) {
        const row = text('label', 'subdivision-option subdivision-legacy', '');
        const checkbox = document.createElement('input'); checkbox.type = 'checkbox'; checkbox.value = value; checkbox.checked = true;
        checkbox.addEventListener('change', () => {
          if (!checkbox.checked) row.remove();
          const chosen = [...optionsRoot.querySelectorAll('input[type="checkbox"]:checked')].map(input => input.value);
          hidden.value = chosen.join(' | ');
          summary.textContent = chosen.length ? `Выбрано: ${chosen.join('; ')}` : 'Ничего не выбрано.';
        });
        row.append(checkbox, text('span', 'subdivision-option-marker', '↳'), text('span', 'subdivision-option-path', `Ранее указано: ${value}`));
        optionsRoot.append(row);
      }
    }
    const checkedValues = [...optionsRoot.querySelectorAll('input[type="checkbox"]:checked')].map(input => input.value);
    hidden.value = checkedValues.join(' | ');
    summary.textContent = checkedValues.length ? `Выбрано: ${checkedValues.join('; ')}` : 'Ничего не выбрано.';
  }
  async function loadOrganizationalUnits() {
    const units = await api('/api/organizational-units');
    state.units = Array.isArray(units) ? units : [];
    const select = $('organizational-unit-select');
    const previous = select.value;
    select.replaceChildren(new Option('Не выбрано', ''));
    const roots = state.units.filter(unit => unit.parentUnitId == null).sort((a,b) => a.name.localeCompare(b.name, 'ru'));
    for (const unit of roots) select.add(new Option(unit.name, String(unit.id)));
    if ([...select.options].some(option => option.value === previous)) select.value = previous;
    renderSubdivisionOptions($('subdivision-select').value);
  }

  const relatedKinds = {
    cars: { title: 'Автомобили', singular: 'Автомобиль', collection: 'cars', fields: [
      ['makeModel','Марка и модель','text',true], ['registrationPlate','Государственный номер','text',false], ['productionYear','Год выпуска','number',false], ['registrationCertificateNumber','Номер СТС','text',false]] },
    'license-categories': { title: 'Водительские категории', singular: 'Категория прав', collection: 'drivingLicenseCategories', fields: [
      ['category','Категория','text',true], ['expiresOn','Действует до','date',false]] },
    relatives: { title: 'Родственники', singular: 'Родственник', collection: 'relatives', fields: [
      ['fullName','ФИО','text',true], ['relationship','Степень родства','text',true], ['dateOfBirth','Дата рождения','date',false], ['workplace','Место работы','text',false], ['position','Должность','text',false], ['schoolOrInstitution','Школа / учебное заведение','text',false], ['birthCertificateNumber','Номер свидетельства о рождении','text',false], ['messengerContactsJson','Контакты в мессенджерах (текст)','text',false]] },
    'position-history': { title: 'Должности', singular: 'Период должности', collection: 'positionHistory', fields: [
      ['position','Должность','text',true], ['organizationOrSubdivision','Организация / подразделение','text',false], ['startDate','Начало периода','date',true], ['endDate','Окончание периода','date',false], ['basisDocument','Документ-основание','text',false]] },
    'classness-history': { title: 'Классность', singular: 'Запись о классности', collection: 'classnessHistory', fields: [
      ['classness','Классность','text',true], ['grantedOn','Дата присвоения','date',true], ['reviewDueOn','Дата проверки','date',false], ['basisDocument','Документ-основание','text',false]] },
    awards: { title: 'Награды', singular: 'Награда', collection: 'awards', fields: [
      ['awardType','Тип награды (state / departmental)','select',true,['state','departmental']], ['name','Наименование награды','text',true], ['awardedOn','Дата награждения','date',false], ['basisDocument','Документ-основание','text',false]] },
    'business-trips': { title: 'Командировки', singular: 'Командировка', collection: 'businessTrips', fields: [
      ['startDate','Дата начала','date',true], ['endDate','Дата окончания','date',true], ['destination','Место командировки','text',true], ['positionOrAssignment','Задача / поручение','text',false], ['basisDocument','Документ-основание','text',false]] },
    messengers: { title: 'Мессенджеры', singular: 'Контакт', collection: 'messengers', fields: [
      ['messenger','Мессенджер','text',true], ['contact','Контакт / имя пользователя','text',true], ['note','Примечание','text',false]] }
  };
  const dateRelatedFields = new Set(['expiresOn','dateOfBirth','startDate','endDate','grantedOn','reviewDueOn','awardedOn']);
  const relatedDeleteKinds = { cars:'cars', 'license-categories':'license-categories', relatives:'relatives', 'position-history':'position-history', 'classness-history':'classness-history', awards:'awards', 'business-trips':'business-trips', messengers:'messengers' };
  function relatedDisplayValue(field, value) {
    if (value == null || value === '') return '—';
    return dateRelatedFields.has(field) ? dateRu(value) : String(value);
  }
  async function attachmentRequest(path, options = {}) {
    const headers = new Headers(options.headers || {});
    if (state.token) headers.set('Authorization', `Bearer ${state.token}`);
    headers.set('Accept', 'application/json');
    // Do not set Content-Type for FormData: the browser supplies the multipart boundary.
    const response = await fetch(path, { ...options, headers, cache: 'no-store' });
    if (response.status === 401) { logout(); throw new Error('Сессия завершена. Войдите снова.'); }
    if (!response.ok) {
      let message = `Ошибка файла (${response.status})`;
      try { const body = await response.json(); message = body.error || body.title || message; } catch (_) {}
      throw new Error(message);
    }
    return response;
  }
  async function loadEmployeeAttachments(employeeId) {
    const section = $('employee-attachments');
    const list = $('employee-attachments-list');
    const error = $('employee-attachments-error');
    list.replaceChildren(); error.textContent = ''; error.classList.add('hidden');
    if (!state.roles.includes('admin')) { section.classList.add('hidden'); return; }
    section.classList.remove('hidden');
    try {
      const response = await attachmentRequest(`/api/employees/${encodeURIComponent(employeeId)}/attachments`);
      const records = await response.json();
      renderEmployeeAttachments(employeeId, Array.isArray(records) ? records : []);
    } catch (err) {
      error.textContent = err.message || 'Не удалось загрузить вложения.';
      error.classList.remove('hidden');
    }
  }
  function attachmentTargetOptions() {
    const details = state.employeeDetails || {};
    const options = [];
    const add = (type, items, labelFn) => {
      for (const item of (Array.isArray(items) ? items : [])) {
        const kindLabel = ({relative:'Родственник',car:'Автомобиль',award:'Награда','position-history':'Должность',classness:'Классность',trip:'Командировка','license-category':'Категория прав',messenger:'Мессенджер'})[type] || type;
        options.push({ type, id: Number(item.id), label: `${kindLabel}: ${labelFn(item)}` });
      }
    };
    add('relative', details.relatives, x => x.fullName || `№${x.id}`);
    add('car', details.cars, x => [x.makeModel, x.registrationPlate].filter(Boolean).join(' · ') || `№${x.id}`);
    add('license-category', details.drivingLicenseCategories, x => x.category || `№${x.id}`);
    add('position-history', details.positionHistory, x => x.position || `№${x.id}`);
    add('classness', details.classnessHistory, x => x.classness || `№${x.id}`);
    add('award', details.awards, x => x.name || `№${x.id}`);
    add('trip', details.businessTrips, x => x.destination || `№${x.id}`);
    add('messenger', details.messengers, x => `${x.messenger || ''} ${x.contact || ''}`.trim() || `№${x.id}`);
    return options;
  }
  function renderEmployeeAttachments(employeeId, records) {
    const root = $('employee-attachments-list'); root.replaceChildren();
    const details = state.employeeDetails || {};
    const allTargets = attachmentTargetOptions();
    const targetTypeByCategory = { car_registration: 'car', birth_certificate: 'relative', award: 'award', relative: 'relative', driving_license: 'license-category' };
    for (const [category, label] of attachmentGroups) {
      const section = text('section', 'attachment-group', '');
      const header = text('div', 'attachment-group-heading', ''); header.append(text('h4', '', label));
      const preferredType = targetTypeByCategory[category];
      const targetOptions = preferredType ? allTargets.filter(o => o.type === preferredType) : [];
      const targetSelect = document.createElement('select'); targetSelect.className = 'attachment-target-select';
      targetSelect.setAttribute('aria-label', `К какой записи прикрепить: ${label}`);
      let selectedType = 'employee', selectedId = 0;
      if (targetOptions.length) {
        for (const option of targetOptions) targetSelect.add(new Option(option.label, `${option.type}:${option.id}`));
        const selectedKey = state.attachmentTargets[category] || targetSelect.options[0]?.value;
        targetSelect.value = [...targetSelect.options].some(o => o.value === selectedKey) ? selectedKey : targetSelect.options[0].value;
        const updateTarget = () => { state.attachmentTargets[category] = targetSelect.value; renderEmployeeAttachments(employeeId, records); };
        targetSelect.addEventListener('change', updateTarget); header.append(targetSelect);
        [selectedType, selectedId] = targetSelect.value.split(':'); selectedId = Number(selectedId);
      }
      const addButton = text('button', 'button button-secondary', 'Добавить скан'); addButton.type = 'button';
      addButton.addEventListener('click', () => chooseAttachment(employeeId, category, selectedType, selectedId)); header.append(addButton); section.append(header);
      const groupRecords = records.filter(r => r.category === category && (r.targetType || 'employee') === selectedType && Number(r.targetId || 0) === selectedId);
      if (!groupRecords.length) section.append(text('p', 'muted small', 'Файлы не прикреплены к выбранной записи.'));
      else {
        const items = text('div', 'attachment-records', '');
        for (const item of groupRecords) {
          const row = text('div', 'attachment-record', ''); const meta = text('div', 'attachment-meta', '');
          meta.append(text('strong', '', item.originalFileName), text('span', 'muted small', `Загружен: ${dateRu(String(item.uploadedUtc || '').slice(0,10))}`));
          const actions = text('div', 'attachment-actions', ''); const download = text('button', 'button button-quiet', 'Скачать');
          download.type = 'button'; download.addEventListener('click', () => downloadAttachment(employeeId, item));
          const remove = text('button', 'button button-quiet danger-button', 'Удалить'); remove.type = 'button'; remove.addEventListener('click', () => deleteAttachment(employeeId, item));
          actions.append(download, remove); row.append(meta, actions); items.append(row);
        }
        section.append(items);
      }
      root.append(section);
    }
  }
  function chooseAttachment(employeeId, category, targetType = 'employee', targetId = 0) {
    const picker = document.createElement('input');
    picker.type = 'file'; picker.accept = '.pdf,.jpg,.jpeg,.png,.webp,application/pdf,image/jpeg,image/png,image/webp';
    picker.addEventListener('change', async () => {
      const file = picker.files && picker.files[0]; if (!file) return;
      if (file.size > 10 * 1024 * 1024) { showAttachmentError('Максимальный размер файла — 10 МБ.'); return; }
      const data = new FormData(); data.append('file', file, file.name);
      try {
        await attachmentRequest(`/api/employees/${encodeURIComponent(employeeId)}/attachments?category=${encodeURIComponent(category)}&targetType=${encodeURIComponent(targetType)}&targetId=${encodeURIComponent(targetId)}`, { method: 'POST', body: data });
        showAttachmentError('');
        await loadEmployeeAttachments(employeeId);
      } catch (err) { showAttachmentError(err.message || 'Не удалось прикрепить файл.'); }
    });
    picker.click();
  }
  async function downloadAttachment(employeeId, item) {
    try {
      const response = await attachmentRequest(`/api/employees/${encodeURIComponent(employeeId)}/attachments/${encodeURIComponent(item.id)}`);
      const blob = await response.blob(); const url = URL.createObjectURL(blob);
      const link = document.createElement('a'); link.href = url; link.download = item.originalFileName || 'document';
      document.body.append(link); link.click(); link.remove(); URL.revokeObjectURL(url);
    } catch (err) { showAttachmentError(err.message || 'Не удалось скачать файл.'); }
  }
  async function deleteAttachment(employeeId, item) {
    if (!window.confirm(`Удалить файл «${item.originalFileName}»?`)) return;
    try {
      await attachmentRequest(`/api/employees/${encodeURIComponent(employeeId)}/attachments/${encodeURIComponent(item.id)}`, { method: 'DELETE', headers: { Accept: 'application/json' } });
      await loadEmployeeAttachments(employeeId);
    } catch (err) { showAttachmentError(err.message || 'Не удалось удалить файл.'); }
  }
  function showAttachmentError(message) {
    const box = $('employee-attachments-error');
    box.textContent = message || ''; box.classList.toggle('hidden', !message);
  }
  async function loadEmployeePhoto(employeeId) {
    const section = $('employee-photo-section'); const image = $('employee-photo-image');
    const placeholder = $('employee-photo-placeholder'); const actions = $('employee-photo-actions');
    section.classList.remove('hidden'); $('employee-photo-error').classList.add('hidden');
    if (state.employeePhotoUrl) { URL.revokeObjectURL(state.employeePhotoUrl); state.employeePhotoUrl = null; }
    image.classList.add('hidden'); image.removeAttribute('src'); placeholder.classList.remove('hidden');
    actions.classList.toggle('hidden', !state.roles.includes('admin'));
    $('delete-employee-photo').classList.add('hidden');
    try {
      const headers = new Headers({ Accept: 'image/jpeg,image/png,image/webp' }); if (state.token) headers.set('Authorization', `Bearer ${state.token}`);
      const response = await fetch(`/api/employees/${encodeURIComponent(employeeId)}/photo`, { headers, cache: 'no-store' });
      if (response.status === 401) { logout(); return; }
      if (response.status === 404) return;
      if (!response.ok) throw new Error(`Не удалось загрузить фотографию (${response.status}).`);
      const blob = await response.blob(); state.employeePhotoUrl = URL.createObjectURL(blob);
      image.src = state.employeePhotoUrl; image.classList.remove('hidden'); placeholder.classList.add('hidden'); $('delete-employee-photo').classList.toggle('hidden', !state.roles.includes('admin'));
    } catch (err) { $('employee-photo-error').textContent = err.message || 'Не удалось загрузить фотографию.'; $('employee-photo-error').classList.remove('hidden'); }
  }
  function chooseEmployeePhoto() {
    if (!state.roles.includes('admin') || state.currentEmployeeId == null) return;
    const picker = document.createElement('input'); picker.type = 'file'; picker.accept = '.jpg,.jpeg,.png,.webp,image/jpeg,image/png,image/webp';
    picker.addEventListener('change', async () => {
      const file = picker.files && picker.files[0]; if (!file) return;
      if (file.size > 5 * 1024 * 1024) { $('employee-photo-error').textContent = 'Максимальный размер фотографии — 5 МБ.'; $('employee-photo-error').classList.remove('hidden'); return; }
      const form = new FormData(); form.append('file', file, file.name);
      try {
        await attachmentRequest(`/api/employees/${encodeURIComponent(state.currentEmployeeId)}/photo`, { method: 'PUT', body: form });
        await loadEmployeePhoto(state.currentEmployeeId);
      } catch (err) { $('employee-photo-error').textContent = err.message || 'Не удалось сохранить фотографию.'; $('employee-photo-error').classList.remove('hidden'); }
    }); picker.click();
  }
  async function deleteEmployeePhoto() {
    if (!state.roles.includes('admin') || state.currentEmployeeId == null) return;
    if (!window.confirm('Удалить фотографию этого сотрудника?')) return;
    try { await attachmentRequest(`/api/employees/${encodeURIComponent(state.currentEmployeeId)}/photo`, { method: 'DELETE' }); await loadEmployeePhoto(state.currentEmployeeId); }
    catch (err) { $('employee-photo-error').textContent = err.message || 'Не удалось удалить фотографию.'; $('employee-photo-error').classList.remove('hidden'); }
  }
  async function openExportTableDialog() {
    $('export-table-error').classList.add('hidden'); $('export-table-dialog').showModal();
    $('export-selected-count').textContent = String(state.selectedEmployeeIds.size);
    const root = $('export-fields-list'); root.replaceChildren(text('p', 'muted', 'Загрузка списка полей…'));
    try {
      state.exportFields = await api('/api/export/fields'); root.replaceChildren();
      const defaults = new Set(['personnelNumber','lastName','firstName','middleName','position','organizationalUnitName','rank','contractStart','contractEnd']);
      for (const field of state.exportFields) {
        const label = document.createElement('label'); label.className = 'export-field-option';
        const input = document.createElement('input'); input.type = 'checkbox'; input.value = field.key; input.checked = defaults.has(field.key);
        label.append(input, text('span', '', field.label)); root.append(label);
      }
      refreshExportPresetOptions();
    } catch (err) { root.replaceChildren(); $('export-table-error').textContent = err.message || 'Не удалось получить список полей.'; $('export-table-error').classList.remove('hidden'); }
  }
  const exportPresetStorageKey = 'kartoteka.exportColumnPresets.v1';
  function readExportPresets() { try { const value = JSON.parse(localStorage.getItem(exportPresetStorageKey) || '{}'); return value && typeof value === 'object' && !Array.isArray(value) ? value : {}; } catch (_) { return {}; } }
  function refreshExportPresetOptions() {
    const select = $('export-presets'); const selected = select.value; select.replaceChildren(new Option('Мои наборы столбцов…', ''));
    for (const name of Object.keys(readExportPresets()).sort((a,b) => a.localeCompare(b, 'ru'))) select.add(new Option(name, name));
    if ([...select.options].some(o => o.value === selected)) select.value = selected;
  }
  function saveExportPreset() {
    const name = window.prompt('Название набора столбцов:'); if (!name || !name.trim()) return;
    const fields = [...$('export-fields-list').querySelectorAll('input[type=checkbox]:checked')].map(input => input.value);
    if (!fields.length) { const error = $('export-table-error'); error.textContent = 'Выберите хотя бы один столбец перед сохранением набора.'; error.classList.remove('hidden'); return; }
    const presets = readExportPresets(); presets[name.trim().slice(0,80)] = fields;
    try { localStorage.setItem(exportPresetStorageKey, JSON.stringify(presets)); refreshExportPresetOptions(); $('export-presets').value = name.trim().slice(0,80); }
    catch (_) { const error = $('export-table-error'); error.textContent = 'Не удалось сохранить набор в этом браузере.'; error.classList.remove('hidden'); }
  }
  function applyExportPreset() {
    const name = $('export-presets').value; if (!name) return;
    const fields = new Set(readExportPresets()[name] || []);
    for (const input of $('export-fields-list').querySelectorAll('input[type=checkbox]')) input.checked = fields.has(input.value);
  }
  function setExportFieldChecks(checked) { for (const input of $('export-fields-list').querySelectorAll('input[type=checkbox]')) input.checked = checked; }
  function getExportRequest(format) {
    const error = $('export-table-error'); error.classList.add('hidden'); error.textContent = '';
    const fields = [...$('export-fields-list').querySelectorAll('input[type=checkbox]:checked')].map(input => input.value);
    if (!fields.length) { error.textContent = 'Выберите хотя бы один столбец.'; error.classList.remove('hidden'); return null; }
    const scope = document.querySelector('input[name="export-scope"]:checked')?.value || 'all';
    const body = { fields, format };
    if (scope === 'selected') {
      if (!state.selectedEmployeeIds.size) { error.textContent = 'Сначала отметьте одного или нескольких сотрудников в списке.'; error.classList.remove('hidden'); return null; }
      body.employeeIds = [...state.selectedEmployeeIds];
    } else if (scope === 'search' && state.query) body.query = state.query;
    return body;
  }
  async function previewTable() {
    const body = getExportRequest('preview'); if (!body) return;
    const button = $('export-table-preview'); button.disabled = true; button.textContent = 'Загрузка…';
    try {
      const headers = new Headers({ Accept: 'application/json', 'Content-Type': 'application/json' }); if (state.token) headers.set('Authorization', `Bearer ${state.token}`);
      const response = await fetch('/api/export/table', { method: 'POST', headers, body: JSON.stringify(body), cache: 'no-store' });
      if (response.status === 401) { logout(); return; }
      if (!response.ok) { let message = `Ошибка формирования таблицы (${response.status})`; try { const data = await response.json(); message = data.error || data.title || message; } catch (_) {} throw new Error(message); }
      const data = await response.json(); state.exportPreview = { headers: data.headers || [], rows: data.rows || [], sortIndex: -1, descending: false };
      $('export-preview-search').value = ''; $('export-preview-section').classList.remove('hidden'); renderExportPreviewTable();
    } catch (err) { const error = $('export-table-error'); error.textContent = err.message || 'Не удалось сформировать таблицу.'; error.classList.remove('hidden'); }
    finally { button.disabled = false; button.textContent = 'Показать таблицу'; }
  }
  function renderExportPreviewTable() {
    const preview = state.exportPreview; const head = $('export-preview-head'); const body = $('export-preview-body'); head.replaceChildren(); body.replaceChildren();
    const query = $('export-preview-search').value.trim().toLocaleLowerCase('ru-RU');
    let rows = preview.rows.map((row, index) => ({ row, index })).filter(item => !query || item.row.some(value => String(value ?? '').toLocaleLowerCase('ru-RU').includes(query)));
    if (preview.sortIndex >= 0) { const index = preview.sortIndex; rows.sort((a,b) => String(a.row[index] ?? '').localeCompare(String(b.row[index] ?? ''), 'ru', { numeric: true, sensitivity: 'base' }) * (preview.descending ? -1 : 1)); }
    const headerRow = document.createElement('tr');
    preview.headers.forEach((label, index) => { const th = document.createElement('th'); const sort = text('button', 'export-sort-button', `${label}${preview.sortIndex === index ? (preview.descending ? ' ↓' : ' ↑') : ''}`); sort.type = 'button'; sort.addEventListener('click', () => { if (preview.sortIndex === index) preview.descending = !preview.descending; else { preview.sortIndex = index; preview.descending = false; } renderExportPreviewTable(); }); th.append(sort); headerRow.append(th); }); head.append(headerRow);
    const shown = rows.slice(0, 500); for (const item of shown) { const tr = document.createElement('tr'); for (const value of item.row) tr.append(text('td', '', value)); body.append(tr); }
    $('export-preview-count').textContent = `Найдено строк: ${rows.length}; показано: ${shown.length}`;
  }
  async function runTableExport() {
    const format = document.querySelector('input[name="export-format"]:checked')?.value || 'docx';
    const body = getExportRequest(format); if (!body) return;
    let printWindow = null;
    if (format === 'print') { printWindow = window.open('', '_blank'); if (!printWindow) { const error = $('export-table-error'); error.textContent = 'Разрешите открытие нового окна для печати.'; error.classList.remove('hidden'); return; } }
    const button = $('export-table-run'); button.disabled = true; button.textContent = 'Формирование…';
    try {
      const headers = new Headers({ Accept: 'application/octet-stream, text/html, application/json', 'Content-Type': 'application/json' }); if (state.token) headers.set('Authorization', `Bearer ${state.token}`);
      const response = await fetch('/api/export/table', { method: 'POST', headers, body: JSON.stringify(body), cache: 'no-store' });
      if (response.status === 401) { logout(); throw new Error('Сессия завершена. Войдите снова.'); }
      if (!response.ok) { let message = `Ошибка экспорта (${response.status})`; try { const data = await response.json(); message = data.error || data.title || message; } catch (_) {} throw new Error(message); }
      if (format === 'print') { const html = await response.text(); printWindow.document.open(); printWindow.document.write(html); printWindow.document.close(); $('export-table-dialog').close(); return; }
      const blob = await response.blob(); const url = URL.createObjectURL(blob); const link = document.createElement('a');
      const ext = format === 'docx' ? 'docx' : format === 'xlsx' ? 'xlsx' : 'csv';
      link.href = url; link.download = `employees_${new Date().toISOString().replace(/[-:T]/g,'').slice(0,14)}.${ext}`; document.body.append(link); link.click(); link.remove(); URL.revokeObjectURL(url); $('export-table-dialog').close();
    } catch (err) { if (printWindow && !printWindow.closed) printWindow.close(); const error = $('export-table-error'); error.textContent = err.message || 'Не удалось сформировать файл.'; error.classList.remove('hidden'); }
    finally { button.disabled = false; button.textContent = 'Сформировать файл'; }
  }

  async function loadEmployeeDetails(employeeId) {
    const box = $('employee-related-list'); box.replaceChildren();
    $('employee-related-error').classList.add('hidden');
    if (!(state.roles.includes('admin') || state.roles.includes('editor'))) {
      const message = await api(`/api/employees/${encodeURIComponent(employeeId)}/details`);
      state.employeeDetails = message; renderRelatedRecords(message, false); return;
    }
    try { state.employeeDetails = await api(`/api/employees/${encodeURIComponent(employeeId)}/details`); renderRelatedRecords(state.employeeDetails, true); }
    catch (error) { $('employee-related-error').textContent = error.message || 'Не удалось загрузить связанные записи.'; $('employee-related-error').classList.remove('hidden'); }
  }
  function renderRelatedRecords(details, canEdit) {
    const root = $('employee-related-list'); root.replaceChildren();
    for (const [kind, config] of Object.entries(relatedKinds)) {
      const section = text('section', 'related-card', '');
      const header = text('div', 'related-card-heading', '');
      header.append(text('h4', '', config.title));
      if (canEdit) { const add = text('button', 'button button-secondary', '+ Добавить'); add.type = 'button'; add.addEventListener('click', () => openRelatedForm(kind, null)); header.append(add); }
      section.append(header);
      const records = Array.isArray(details[config.collection]) ? details[config.collection] : [];
      if (!records.length) section.append(text('p', 'muted small', 'Записей пока нет.'));
      else {
        const list = text('div', 'related-records', '');
        for (const record of records) {
          const item = text('article', 'related-record', ''); const values = text('div', 'related-record-values', '');
          const summaryFields = config.fields.slice(0, 3);
          for (const [field, label] of summaryFields) { const value = record[field]; if (value != null && value !== '') values.append(text('div', 'related-value', `${label}: ${relatedDisplayValue(field, value)}`)); }
          item.append(values);
          if (canEdit) {
            const actions = text('div', 'related-actions', '');
            const edit = text('button', 'button button-quiet', 'Изменить'); edit.type = 'button'; edit.addEventListener('click', () => openRelatedForm(kind, record));
            const del = text('button', 'button button-quiet danger-button', 'Удалить'); del.type = 'button'; del.addEventListener('click', () => deleteRelatedRecord(kind, record));
            actions.append(edit, del); item.append(actions);
          }
          list.append(item);
        }
        section.append(list);
      }
      root.append(section);
    }
  }
  async function openRelatedForm(kind, record) {
    const config = relatedKinds[kind]; if (!config || state.currentEmployeeId == null) return;
    state.currentRelatedKind = kind; state.currentRelatedId = record?.id ?? null;
    $('related-form-title').textContent = record ? `Изменить: ${config.singular.toLowerCase()}` : `Добавить: ${config.singular.toLowerCase()}`;
    $('related-form-error').textContent = ''; $('related-form-error').classList.add('hidden');
    const fieldsBox = $('related-form-fields'); fieldsBox.replaceChildren();
    for (const [field, label, type, required, options] of config.fields) {
      const lab = text('label', '', label + (required ? ' *' : ''));
      let input;
      if (type === 'select') { input = document.createElement('select'); input.add(new Option('Выберите значение', '')); for (const opt of options) input.add(new Option(opt === 'state' ? 'Государственная' : 'Ведомственная', opt)); }
      else { input = document.createElement('input'); input.type = type === 'date' ? 'text' : type; if (type === 'date') { input.placeholder = 'ДД.ММ.ГГГГ'; input.maxLength = 10; input.inputMode = 'numeric'; input.dataset.relatedDate = 'true'; } if (type === 'number') { input.min = '1886'; input.max = '2100'; input.step = '1'; } }
      input.name = field; input.required = required; if (type === 'text') input.maxLength = 500; input.value = record?.[field] == null ? '' : (type === 'date' ? isoToRu(record[field]) : String(record[field]));
      lab.append(input); fieldsBox.append(lab);
    }
    const relativeCustomSection = $('relative-custom-fields-section');
    const relativeCustomRoot = $('relative-custom-fields');
    relativeCustomRoot.replaceChildren();
    relativeCustomSection.classList.add('hidden');
    if (kind === 'relatives') {
      try {
        const definitions = await api('/api/custom-fields?scope=relative');
        const existingValues = record?.id == null ? [] : await api(`/api/employees/${encodeURIComponent(state.currentEmployeeId)}/relatives/${encodeURIComponent(record.id)}/custom-fields`);
        const valueMap = new Map(existingValues.map(v => [Number(v.id), v.value]));
        for (const def of definitions) {
          const lab = text('label', '', def.label + (def.isRequired ? ' *' : ''));
          let input;
          if (def.dataType === 'boolean') {
            input = document.createElement('input'); input.type = 'checkbox'; input.checked = String(valueMap.get(Number(def.id)) || '').toLowerCase() === 'true';
          } else {
            input = document.createElement('input'); input.type = def.dataType === 'number' ? 'number' : 'text';
            if (def.dataType === 'date') { input.placeholder = 'ДД.ММ.ГГГГ'; input.maxLength = 10; input.inputMode = 'numeric'; }
            input.value = def.dataType === 'date' ? isoToRu(valueMap.get(Number(def.id)) || '') : (valueMap.get(Number(def.id)) || '');
          }
          input.dataset.relativeCustomFieldId = String(def.id); input.dataset.relativeCustomFieldType = def.dataType;
          if (def.isRequired) input.required = true;
          lab.append(input); relativeCustomRoot.append(lab);
        }
        relativeCustomSection.classList.toggle('hidden', definitions.length === 0);
      } catch (error) { setRelatedError(`Не удалось загрузить дополнительные поля родственника: ${error.message}`); }
    }
    $('related-form-dialog').showModal();
  }
  async function saveRelatedForm(event) {
    event.preventDefault(); if (state.savingRelated) return;
    if (!(state.roles.includes('admin') || state.roles.includes('editor'))) { setRelatedError('Недостаточно прав для изменения записей.'); return; }
    const kind = state.currentRelatedKind, config = relatedKinds[kind], employeeId = state.currentEmployeeId;
    if (!config || employeeId == null) return;
    const payload = {};
    try {
      for (const [field, label, type, required] of config.fields) {
        const input = $('related-form').elements.namedItem(field); const raw = input.value.trim();
        if (required && !raw) throw new Error(`Заполните поле «${label}».`);
        if (type === 'date') payload[field] = parseRuDate(raw, label);
        else if (type === 'number') { if (!raw) payload[field] = null; else { const number = Number(raw); if (!Number.isInteger(number)) throw new Error(`Поле «${label}» должно быть целым числом.`); payload[field] = number; } }
        else payload[field] = (type === 'select' ? raw : capitalizeTextValue(field, raw)) || null;
      }
      if (kind === 'business-trips' && payload.endDate < payload.startDate) throw new Error('Дата окончания командировки не может быть раньше даты начала.');
      if (kind === 'position-history' && payload.endDate && payload.endDate < payload.startDate) throw new Error('Дата окончания должности не может быть раньше даты начала.');
      if (kind === 'classness-history' && payload.reviewDueOn && payload.reviewDueOn < payload.grantedOn) throw new Error('Дата проверки не может быть раньше даты присвоения.');
    } catch (error) { setRelatedError(error.message || 'Проверьте поля формы.'); return; }
    state.savingRelated = true; $('related-form-save').disabled = true; $('related-form-cancel').disabled = true;
    try {
      const editingId = state.currentRelatedId;
      const path = `/api/employees/${encodeURIComponent(employeeId)}/details/${kind}${editingId == null ? '' : `/${encodeURIComponent(editingId)}`}`;
      const savedRecord = await api(path, { method: editingId == null ? 'POST' : 'PUT', body: JSON.stringify(payload) });
      const savedId = editingId == null ? savedRecord.id : editingId;
      state.currentRelatedId = savedId;
      if (kind === 'relatives') {
        const customValues = [...$('relative-custom-fields').querySelectorAll('[data-relative-custom-field-id]')].map(input => ({
          fieldDefinitionId: Number(input.dataset.relativeCustomFieldId),
          value: input.type === 'checkbox' ? String(input.checked) : input.value.trim()
        }));
        if (customValues.length) await api(`/api/employees/${encodeURIComponent(employeeId)}/relatives/${encodeURIComponent(savedId)}/custom-fields`, {
          method: 'PUT', body: JSON.stringify({ values: customValues })
        });
      }
      $('related-form-dialog').close(); await loadEmployeeDetails(employeeId); await refreshAll();
      showStatus(editingId == null ? 'Связанная запись добавлена.' : 'Связанная запись изменена.');
    } catch (error) { setRelatedError(error.message || 'Не удалось сохранить запись.'); }
    finally { state.savingRelated = false; $('related-form-save').disabled = false; $('related-form-cancel').disabled = false; }
  }
  function setRelatedError(message) { const box = $('related-form-error'); box.textContent = message || ''; box.classList.toggle('hidden', !message); }
  async function deleteRelatedRecord(kind, record) {
    if (!window.confirm(`Удалить запись «${relatedKinds[kind].singular}»? Это действие нельзя отменить.`)) return;
    try { await api(`/api/employees/${encodeURIComponent(state.currentEmployeeId)}/details/${relatedDeleteKinds[kind]}/${encodeURIComponent(record.id)}`, { method: 'DELETE' }); await loadEmployeeDetails(state.currentEmployeeId); await refreshAll(); showStatus('Связанная запись удалена.'); }
    catch (error) { const box = $('employee-related-error'); box.textContent = error.message || 'Не удалось удалить запись.'; box.classList.remove('hidden'); }
  }

  const employeeFormFields = [
    'personnelNumber','lastName','firstName','middleName','dateOfBirth','passportSeries','passportNumber','passportIssuedBy','passportIssuedOn','maritalStatus',
    'position','subdivision','rank','transferDate','transferFrom','accessForm','accessGrantedOn',
    'accessIssuedBy','contractStart','contractTermMonths','contractEnd','militaryIdNumber',
    'militaryRegistrationOffice','militaryRegistrationCategory','militaryRegistrationStatus'
  ];
  const dateFields = new Set(['dateOfBirth','passportIssuedOn','transferDate','accessGrantedOn','contractStart','contractEnd']);
  function normalizeDateInput(value) {
    const raw = String(value || '').trim();
    if (/^\d{8}$/.test(raw)) return `${raw.slice(0,2)}.${raw.slice(2,4)}.${raw.slice(4)}`;
    return raw;
  }
  function parseRuDate(value, label) {
    const raw = String(value || '').trim();
    if (!raw) return null;
    const match = /^(\d{2})\.(\d{2})\.(\d{4})$/.exec(raw);
    if (!match) throw new Error(`Поле «${label}»: введите дату в формате ДД.ММ.ГГГГ.`);
    const day = Number(match[1]), month = Number(match[2]), year = Number(match[3]);
    const date = new Date(Date.UTC(year, month - 1, day));
    if (date.getUTCFullYear() !== year || date.getUTCMonth() !== month - 1 || date.getUTCDate() !== day || year < 1900 || year > 2200)
      throw new Error(`Поле «${label}»: такой календарной даты не существует.`);
    return `${match[3]}-${match[2]}-${match[1]}`;
  }
  function isoToRu(value) {
    if (!value) return '';
    const parts = String(value).slice(0, 10).split('-');
    return parts.length === 3 ? `${parts[2]}.${parts[1]}.${parts[0]}` : '';
  }
  function setFormError(message) {
    const box = $('employee-form-error');
    box.textContent = message || '';
    box.classList.toggle('hidden', !message);
  }
  async function openEmployeeForm(id) {
    if (!(state.roles.includes('admin') || state.roles.includes('editor'))) {
      showStatus('У вашей роли нет права редактировать карточки.'); return;
    }
    const form = $('employee-form'); form.reset(); setFormError('');
    try { await loadOrganizationalUnits(); } catch (error) { showStatus(`Не удалось загрузить справочник подразделений: ${error.message}`); return; }
    for (const input of form.querySelectorAll('[aria-invalid="true"]')) input.removeAttribute('aria-invalid');
    state.currentEmployeeId = id == null ? null : id;
    $('employee-form-title').textContent = id == null ? 'Новый сотрудник' : 'Редактирование карточки';
    $('save-employee-button').textContent = id == null ? 'Создать карточку' : 'Сохранить изменения';
    if (id != null) {
      try {
        const employee = await api(`/api/employees/${encodeURIComponent(id)}`);
        for (const name of employeeFormFields) {
          const input = form.elements.namedItem(name);
          if (!input) continue;
          input.value = dateFields.has(name) ? isoToRu(employee[name]) : (employee[name] == null ? '' : String(employee[name]));
        }
        const unitSelect = $('organizational-unit-select');
        const originalUnitId = employee.organizationalUnitId == null ? null : Number(employee.organizationalUnitId);
        const rootId = originalUnitId == null ? null : rootUnitId(originalUnitId);
        unitSelect.value = rootId == null ? '' : String(rootId);
        let subdivisionValue = employee.subdivision || (originalUnitId != null && rootId != null && originalUnitId !== rootId ? relativeUnitPath(originalUnitId, rootId) : '');
        renderSubdivisionOptions(subdivisionValue);
      } catch (error) { showStatus(error.message || 'Не удалось загрузить карточку для редактирования.'); return; }
    }
    try { await loadCustomFieldEditor(id); }
    catch (error) { setFormError(`Не удалось загрузить дополнительные поля: ${error.message}`); }
    $('employee-dialog').close();
    $('employee-form-dialog').showModal();
    const first = id == null ? form.elements.namedItem('lastName') : form.elements.namedItem('lastName');
    if (first) first.focus();
  }
  async function saveEmployeeForm(event) {
    event.preventDefault();
    if (state.savingEmployee) return;
    if (!(state.roles.includes('admin') || state.roles.includes('editor'))) { setFormError('Недостаточно прав для сохранения.'); return; }
    const form = $('employee-form');
    const saveButton = $('save-employee-button');
    setFormError('');
    for (const input of form.querySelectorAll('[aria-invalid="true"]')) input.removeAttribute('aria-invalid');
    let payload = {};
    try {
      for (const name of employeeFormFields) {
        const input = form.elements.namedItem(name);
        if (!input) continue;
        const value = input.value.trim();
        if (dateFields.has(name)) payload[name] = parseRuDate(value, input.closest('label')?.firstChild?.textContent?.replace('*','').trim() || name);
        else if (name === 'contractTermMonths') {
          if (!value) payload[name] = null;
          else {
            const number = Number(value);
            if (!Number.isInteger(number) || number < 1 || number > 600) throw new Error('Срок контракта должен быть целым числом от 1 до 600 месяцев.');
            payload[name] = number;
          }
        } else { const normalized = capitalizeTextValue(name, value); payload[name] = normalized || (['personnelNumber','lastName','firstName'].includes(name) ? '' : null); }
      }
      const unitValue = $('organizational-unit-select').value;
      payload.organizationalUnitId = unitValue ? Number(unitValue) : null;
      if (!payload.lastName) throw new Error('Заполните фамилию.');
      if (!payload.firstName) throw new Error('Заполните имя.');
      if (payload.contractTermMonths != null && payload.contractStart == null) throw new Error('Укажите дату начала контракта.');
      if (payload.contractStart && payload.contractEnd && payload.contractEnd < payload.contractStart) throw new Error('Дата окончания контракта не может быть раньше даты начала.');
      if (payload.dateOfBirth && payload.dateOfBirth > new Date().toISOString().slice(0,10)) throw new Error('Дата рождения не может быть в будущем.');
    } catch (error) {
      setFormError(error.message || 'Проверьте введённые данные.');
      const invalidDate = [...form.querySelectorAll('[data-date]')].find(input => {
        try { parseRuDate(input.value, input.name); return false; } catch (_) { return Boolean(input.value.trim()); }
      });
      if (invalidDate) { invalidDate.setAttribute('aria-invalid','true'); invalidDate.focus(); }
      return;
    }
    state.savingEmployee = true; saveButton.disabled = true; $('cancel-employee-form').disabled = true; $('cancel-employee-form-top').disabled = true;
    try {
      const editingId = state.currentEmployeeId;
      const path = editingId == null ? '/api/employees' : `/api/employees/${encodeURIComponent(editingId)}`;
      const savedEmployee = await api(path, { method: editingId == null ? 'POST' : 'PUT', body: JSON.stringify(payload) });
      const savedId = editingId == null ? savedEmployee.id : editingId;
      // Persist custom values only after the employee row exists. If this request fails,
      // retain the new ID so retrying cannot create a duplicate employee.
      state.currentEmployeeId = savedId;
      const customValues = [...$('custom-fields-editor').querySelectorAll('[data-custom-field-id]')].map(input => ({
        fieldDefinitionId: Number(input.dataset.customFieldId),
        value: input.type === 'checkbox' ? String(input.checked) : input.value.trim()
      }));
      if (customValues.length) {
        await api(`/api/employees/${encodeURIComponent(savedId)}/custom-fields`, {
          method: 'PUT', body: JSON.stringify({ values: customValues })
        });
      }
      $('employee-form-dialog').close();
      showStatus(editingId == null ? 'Карточка сотрудника создана.' : 'Изменения сохранены.');
      await refreshAll();
      if (savedId != null) await openEmployee(savedId);
    } catch (error) {
      setFormError(error.message || 'Не удалось сохранить карточку.');
    } finally {
      state.savingEmployee = false; saveButton.disabled = false; $('cancel-employee-form').disabled = false; $('cancel-employee-form-top').disabled = false;
    }
  }
})();
