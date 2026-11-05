// Lưu tạm form đang soạn (form có data-pm-draft="khóa"): tự lưu vào trình duyệt khi gõ, mở lại trang thì hỏi
// "Tiếp tục soạn / Bỏ bản nháp"; rời trang khi còn thay đổi chưa lưu thì trình duyệt hỏi lại.
// Bản tạm chỉ nằm trên máy + trình duyệt đang dùng, riêng từng tài khoản (meta pm-user), tự xóa sau 7 ngày
// hoặc khi lưu thành công (gửi form rồi sang trang không còn form đó). Không lưu mã chống giả mạo, ô xác nhận cảnh báo.
(function () {
  'use strict';
  var MAX_AGE = 7 * 24 * 3600 * 1000;
  var SKIP = ['__RequestVerificationToken', 'AcknowledgeWarnings', 'WarningsKey', 'intent'];
  var meta = document.querySelector('meta[name="pm-user"]');
  var user = meta ? meta.getAttribute('content') : '';
  if (!user) return;
  var prefix = 'pm-draft:' + user + ':';

  function get(key) {
    try { return JSON.parse(window.localStorage.getItem(prefix + key) || 'null'); } catch (e) { return null; }
  }
  function put(key, value) {
    try { window.localStorage.setItem(prefix + key, JSON.stringify(value)); } catch (e) { /* hết chỗ / bị chặn: bỏ qua */ }
  }
  function drop(key) {
    try { window.localStorage.removeItem(prefix + key); } catch (e) { /* bỏ qua */ }
  }
  function pending() {
    try { return JSON.parse(window.sessionStorage.getItem('pm-draft-submitted') || '[]'); } catch (e) { return []; }
  }
  function setPending(list) {
    try { window.sessionStorage.setItem('pm-draft-submitted', JSON.stringify(list)); } catch (e) { /* bỏ qua */ }
  }

  // ---- Dọn bản tạm quá 7 ngày ----
  try {
    var now = Date.now();
    for (var i = window.localStorage.length - 1; i >= 0; i--) {
      var k = window.localStorage.key(i);
      if (!k || k.indexOf('pm-draft:') !== 0) continue;
      var v = null;
      try { v = JSON.parse(window.localStorage.getItem(k) || 'null'); } catch (e) { v = null; }
      if (!v || !v.t || now - v.t > MAX_AGE) window.localStorage.removeItem(k);
    }
  } catch (e) { /* bỏ qua */ }

  var forms = Array.prototype.slice.call(document.querySelectorAll('form[data-pm-draft]'));
  var keysHere = forms.map(function (f) { return f.getAttribute('data-pm-draft'); });

  // ---- Vừa gửi form: sang trang không còn form đó = lưu thành công → xóa bản tạm; còn form (server báo lỗi) → giữ ----
  var justSubmitted = pending();
  justSubmitted.forEach(function (key) {
    if (keysHere.indexOf(key) < 0) drop(key);
  });
  setPending([]);

  var serverErrors = !!document.querySelector('[data-pm-model-errors]');
  var dirty = false;
  var submitting = false;

  window.addEventListener('beforeunload', function (e) {
    if (!dirty || submitting) return;
    e.preventDefault();
    e.returnValue = '';
  });

  forms.forEach(initForm);

  function fields(form) {
    return Array.prototype.filter.call(form.elements, function (el) {
      if (!el.name || SKIP.indexOf(el.name) >= 0 || el.closest('template')) return false;
      var type = (el.type || '').toLowerCase();
      return ['file', 'password', 'submit', 'button', 'reset'].indexOf(type) < 0;
    });
  }

  function snapshot(form) {
    var data = { t: Date.now(), rows: {}, values: [], checks: [] };
    fields(form).forEach(function (el) {
      var type = (el.type || '').toLowerCase();
      if (type === 'checkbox' || type === 'radio') data.checks.push([el.name, el.value, el.checked]);
      else data.values.push([el.name, el.value]);
    });
    // Dòng thêm/bỏ được (catalog.js): ghi lại chỉ số từng dòng theo ô ẩn "Tiền tố.Index"
    form.querySelectorAll('[data-pm-repeater]').forEach(function (box) {
      var list = [];
      var namePrefix = null;
      Array.prototype.forEach.call(box.children, function (row) {
        if (!row.matches('[data-pm-row]')) return;
        var idx = row.querySelector('input[type=hidden][name$=".Index"]');
        if (!idx) return;
        namePrefix = idx.name.slice(0, -'.Index'.length);
        list.push(idx.value);
      });
      data.rows[box.getAttribute('data-pm-repeater')] = { prefix: namePrefix, indices: list };
    });
    return data;
  }

  function lineCount(data) {
    var n = 0;
    Object.keys(data.rows || {}).forEach(function (k) { n += (data.rows[k].indices || []).length; });
    return n;
  }

  function restore(form, data) {
    var rename = {};
    Object.keys(data.rows || {}).forEach(function (name) {
      var saved = data.rows[name];
      var box = form.querySelector('[data-pm-repeater="' + name + '"]');
      var add = form.querySelector('[data-pm-add="' + name + '"]');
      if (!box || !add || !saved) return;
      var prefixGuess = saved.prefix;
      var wanted = saved.indices || [];
      // Giữ dòng đang có cùng chỉ số (ví dụ dòng theo đơn đặt hàng do server dựng), bỏ dòng không có trong bản tạm,
      // thêm dòng mới (từ mẫu) cho chỉ số còn thiếu rồi đổi tên ô theo chỉ số mới
      var present = [];
      Array.prototype.slice.call(box.children).forEach(function (row) {
        if (!row.matches('[data-pm-row]')) return;
        var idx = row.querySelector('input[type=hidden][name$=".Index"]');
        if (idx && wanted.indexOf(idx.value) >= 0) present.push(idx.value);
        else row.remove();
      });
      wanted.forEach(function (oldIdx) {
        if (present.indexOf(oldIdx) >= 0) return;
        add.click();
        var rows = Array.prototype.filter.call(box.children, function (r) { return r.matches('[data-pm-row]'); });
        var row = rows[rows.length - 1];
        var idx = row && row.querySelector('input[type=hidden][name$=".Index"]');
        if (!idx) return;
        var p = prefixGuess || idx.name.slice(0, -'.Index'.length);
        rename[p + '[' + oldIdx + ']'] = p + '[' + idx.value + ']';
      });
    });
    function mapName(n) {
      var keys = Object.keys(rename);
      for (var i = 0; i < keys.length; i++) {
        if (n.indexOf(keys[i]) === 0) return rename[keys[i]] + n.slice(keys[i].length);
      }
      return n;
    }
    function find(name, value) {
      var list = form.querySelectorAll('[name="' + name.replace(/(["\\])/g, '\\$1') + '"]');
      return Array.prototype.filter.call(list, function (el) {
        return !el.closest('template') && (value === undefined || el.value === value);
      });
    }
    // Lượt 1: ô chọn (đổi thuốc → nạp lại đơn vị), lượt 2: ô gõ, ô ẩn, lượt 3: ô tích
    var values = (data.values || []).map(function (v) { return [mapName(v[0]), v[1]]; });
    values.forEach(function (v) {
      find(v[0]).forEach(function (el) {
        if (el.tagName !== 'SELECT' || el.type === 'hidden') return;
        el.value = v[1];
        el.dispatchEvent(new Event('change', { bubbles: true }));
      });
    });
    values.forEach(function (v) {
      find(v[0]).forEach(function (el) {
        if (el.tagName === 'SELECT' || el.name.slice(-6) === '.Index') return;
        el.value = v[1];
        el.dispatchEvent(new Event('input', { bubbles: true }));
        el.dispatchEvent(new Event('change', { bubbles: true }));
      });
    });
    (data.checks || []).forEach(function (c) {
      find(mapName(c[0]), c[1]).forEach(function (el) {
        if (el.checked === c[2]) return;
        el.checked = c[2];
        el.dispatchEvent(new Event('change', { bubbles: true }));
      });
    });
  }

  function initForm(form) {
    var key = form.getAttribute('data-pm-draft');
    var paused = false;
    var timer = null;

    function save() {
      if (paused || !dirty) return;
      put(key, snapshot(form));
    }
    function later() {
      if (timer) window.clearTimeout(timer);
      timer = window.setTimeout(save, 400);
    }

    form.addEventListener('input', function (e) { if (e.isTrusted) { dirty = true; later(); } });
    form.addEventListener('change', function (e) { if (e.isTrusted) { dirty = true; later(); } });
    form.addEventListener('click', function (e) {
      if (e.target.closest('[data-pm-add], [data-pm-remove]')) { dirty = true; later(); }
    });
    form.addEventListener('submit', function (e) {
      submitting = true;
      save();
      var list = pending();
      if (list.indexOf(key) < 0) list.push(key);
      setPending(list);
      // Kiểm tra trên trình duyệt chặn gửi (còn ô sai) → vẫn ở trang này
      window.setTimeout(function () {
        if (!e.defaultPrevented) return;
        submitting = false;
        setPending(pending().filter(function (k) { return k !== key; }));
      }, 0);
    });

    // Vừa gửi mà server trả lại chính form này (báo lỗi, hoặc "Kiểm tra đơn"): số liệu đang hiện là số vừa gửi
    // → giữ làm bản tạm, không hỏi khôi phục
    if (serverErrors || justSubmitted.indexOf(key) >= 0) {
      dirty = true;
      put(key, snapshot(form));
      return;
    }

    var draft = get(key);
    if (!draft || !draft.values) return;
    paused = true;
    var at = new Date(draft.t);
    var when = ('0' + at.getHours()).slice(-2) + ':' + ('0' + at.getMinutes()).slice(-2)
      + (at.toDateString() === new Date().toDateString() ? '' : ' ngày ' + at.getDate() + '/' + (at.getMonth() + 1));
    var lines = lineCount(draft);

    var bar = document.createElement('div');
    bar.className = 'pm-alert calm d-flex flex-wrap align-items-center justify-content-between gap-2';
    bar.setAttribute('role', 'status');
    var text = document.createElement('span');
    text.textContent = 'Bạn có nội dung đang soạn dở lúc ' + when + (lines > 0 ? ' (' + lines + ' dòng)' : '') + '.';
    var actions = document.createElement('span');
    actions.className = 'd-flex gap-2';
    var resume = document.createElement('button');
    resume.type = 'button';
    resume.className = 'btn btn-sm btn-primary';
    resume.textContent = 'Tiếp tục soạn';
    var discard = document.createElement('button');
    discard.type = 'button';
    discard.className = 'btn btn-sm btn-outline-secondary';
    discard.textContent = 'Bỏ bản nháp này';
    actions.appendChild(resume);
    actions.appendChild(discard);
    bar.appendChild(text);
    bar.appendChild(actions);
    form.parentNode.insertBefore(bar, form);

    resume.addEventListener('click', function () {
      restore(form, draft);
      bar.remove();
      paused = false;
      dirty = true;
      save();
    });
    discard.addEventListener('click', function () {
      drop(key);
      bar.remove();
      paused = false;
    });
  }
})();
