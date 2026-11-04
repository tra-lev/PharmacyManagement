// Hành vi giao diện dùng chung. Không chứa nghiệp vụ: mọi kiểm tra quyền và dữ liệu nằm ở server.
(function () {
  'use strict';

  function store(action, key, value) {
    try {
      if (action === 'get') return window.localStorage.getItem(key);
      window.localStorage.setItem(key, value);
    } catch (e) { /* trình duyệt chặn lưu trữ: bỏ qua */ }
    return null;
  }

  // ---- Ô mã 6 số: chỉ giữ chữ số, tối đa 6 ----
  document.querySelectorAll('input.pm-code').forEach(function (input) {
    input.addEventListener('input', function () {
      var digits = input.value.replace(/\D/g, '').slice(0, 6);
      if (digits !== input.value) input.value = digits;
    });
  });

  // ---- Danh sách điều kiện mật khẩu (khớp cấu hình Identity trong Program.cs) ----
  document.querySelectorAll('[data-pm-rules-for]').forEach(function (list) {
    var input = document.getElementById(list.getAttribute('data-pm-rules-for'));
    if (!input) return;
    var checks = {
      length: function (v) { return v.length >= 8; },
      digit: function (v) { return /\d/.test(v); },
      lower: function (v) { return /[a-z]/.test(v); },
      upper: function (v) { return /[A-Z]/.test(v); }
    };
    function update() {
      list.querySelectorAll('[data-rule]').forEach(function (li) {
        var fn = checks[li.getAttribute('data-rule')];
        var ok = fn ? fn(input.value) : false;
        li.classList.toggle('ok', ok);
      });
    }
    input.addEventListener('input', update);
    update();
  });

  // ---- Nút hiện/ẩn mật khẩu ----
  document.querySelectorAll('[data-pm-toggle-password]').forEach(function (btn) {
    btn.addEventListener('click', function () {
      var input = document.getElementById(btn.getAttribute('data-pm-toggle-password'));
      if (!input) return;
      var show = input.type === 'password';
      input.type = show ? 'text' : 'password';
      btn.textContent = show ? 'Ẩn' : 'Hiện';
      btn.setAttribute('aria-pressed', show ? 'true' : 'false');
    });
  });

  // ---- Nhóm menu: mở/đóng, nhớ theo trình duyệt (nhóm chứa trang hiện tại luôn mở) ----
  document.querySelectorAll('[data-pm-group]').forEach(function (btn) {
    var key = 'pm.nav.' + btn.getAttribute('data-pm-group');
    var panel = document.getElementById(btn.getAttribute('aria-controls'));
    if (!panel) return;
    function set(open) {
      btn.setAttribute('aria-expanded', open ? 'true' : 'false');
      panel.hidden = !open;
    }
    var hasActive = panel.querySelector('.active') !== null;
    var saved = store('get', key);
    if (!hasActive && (saved === '0' || saved === '1')) set(saved === '1');
    btn.addEventListener('click', function () {
      var open = btn.getAttribute('aria-expanded') !== 'true';
      set(open);
      store('set', key, open ? '1' : '0');
    });
  });

  // ---- Sidebar dạng ngăn kéo trên màn hình nhỏ ----
  var sidebar = document.getElementById('pm-sidebar');
  var menuBtn = document.querySelector('[data-pm-menu]');
  if (sidebar && menuBtn) {
    menuBtn.addEventListener('click', function () {
      var open = !sidebar.classList.contains('open');
      sidebar.classList.toggle('open', open);
      menuBtn.setAttribute('aria-expanded', open ? 'true' : 'false');
    });
    document.addEventListener('keydown', function (e) {
      if (e.key === 'Escape' && sidebar.classList.contains('open')) {
        sidebar.classList.remove('open');
        menuBtn.setAttribute('aria-expanded', 'false');
        menuBtn.focus();
      }
    });
  }

  // ---- Thông báo nhanh: thông báo thành công tự ẩn sau 6 giây, lỗi giữ đến khi đóng ----
  document.querySelectorAll('.pm-toast').forEach(function (toast) {
    var close = toast.querySelector('[data-pm-dismiss]');
    if (close) close.addEventListener('click', function () { toast.remove(); });
    if (toast.hasAttribute('data-pm-autohide')) {
      window.setTimeout(function () { toast.remove(); }, 6000);
    }
  });

  // ---- Lỗi form từ server (_Toasts.cshtml, data-pm-model-errors): đã báo ở đầu màn hình →
  //      ẩn khung lỗi lặp lại trong form, viền đỏ các ô lỗi, đưa con trỏ vào ô lỗi đầu tiên đang hiện ----
  var modelErrors = document.querySelector('[data-pm-model-errors]');
  if (modelErrors) {
    document.querySelectorAll('.validation-summary-errors').forEach(function (summary) {
      // Đổi về trạng thái "không lỗi" (không ẩn hẳn) để kiểm tra trên trình duyệt vẫn hiện lại được khi cần
      summary.classList.remove('validation-summary-errors');
      summary.classList.add('validation-summary-valid');
      var list = summary.querySelector('ul');
      if (list) list.innerHTML = '';
    });
    var firstInvalid = null;
    (modelErrors.getAttribute('data-pm-invalid') || '').split('|').forEach(function (name) {
      if (!name) return;
      document.querySelectorAll('[name="' + name.replace(/(["\\])/g, '\\$1') + '"]').forEach(function (field) {
        if (field.type === 'hidden') return;
        field.setAttribute('aria-invalid', 'true');
        if (!firstInvalid && field.offsetParent !== null && !field.disabled) firstInvalid = field;
      });
    });
    document.addEventListener('input', function (e) {
      if (e.target && e.target.getAttribute && e.target.getAttribute('aria-invalid') === 'true') e.target.removeAttribute('aria-invalid');
    });
    // Sau khi các script của trang (chọn phương thức thu tiền, ô gõ tìm…) dựng xong giao diện
    window.setTimeout(function () {
      if (firstInvalid && firstInvalid.offsetParent !== null) firstInvalid.focus({ preventScroll: false });
    }, 0);
  }

  // ---- Lỗi kiểm tra trên trình duyệt (jQuery Validation) khi bấm gửi form: gom lên góc trên bên phải như lỗi từ server ----
  //      Dòng lỗi dưới từng ô giữ nguyên; khung tóm tắt lỗi trong form không hiện lặp lại (site.css).
  //      Cửa sổ Thêm khách hàng (data-pm-quick-customer) tự báo lỗi trong cửa sổ.
  function toastBox() {
    var box = document.querySelector('.pm-toasts');
    if (!box) {
      box = document.createElement('div');
      box.className = 'pm-toasts';
      document.body.appendChild(box);
    }
    return box;
  }
  function showErrorToast(messages) {
    document.querySelectorAll('[data-pm-model-errors], [data-pm-client-errors]').forEach(function (t) { t.remove(); });
    if (messages.length === 0) return;
    var toast = document.createElement('div');
    toast.className = 'pm-toast bad';
    toast.setAttribute('role', 'alert');
    toast.setAttribute('data-pm-client-errors', '');
    var icon = document.createElement('i');
    icon.className = 'bi bi-x-circle';
    icon.setAttribute('aria-hidden', 'true');
    var body = document.createElement('div');
    if (messages.length === 1) {
      body.textContent = messages[0];
    } else {
      var title = document.createElement('p');
      title.className = 'fw-semibold mb-1';
      title.textContent = 'Chưa lưu được — cần sửa ' + messages.length + ' chỗ:';
      var ul = document.createElement('ul');
      messages.forEach(function (m) {
        var li = document.createElement('li');
        li.textContent = m;
        ul.appendChild(li);
      });
      body.appendChild(title);
      body.appendChild(ul);
    }
    var close = document.createElement('button');
    close.type = 'button';
    close.setAttribute('aria-label', 'Đóng thông báo');
    close.innerHTML = '&times;';
    close.addEventListener('click', function () { toast.remove(); });
    toast.appendChild(icon);
    toast.appendChild(body);
    toast.appendChild(close);
    toastBox().prepend(toast);
  }
  // Chỉ trang có khung ứng dụng (_Layout có menu); trang đăng nhập, kích hoạt… giữ cách báo lỗi trong form
  if (window.jQuery && document.querySelector('.pm-main')) {
    // jquery.validate gọi triggerHandler("invalid-form") trên chính form → gắn trực tiếp vào từng form
    window.jQuery('form').not('[data-pm-quick-customer]').on('invalid-form.validate', function (e, validator) {
      var messages = [];
      (validator && validator.errorList || []).forEach(function (err) {
        var text = err.message || 'Giá trị nhập chưa hợp lệ.';
        if (messages.indexOf(text) < 0) messages.push(text);
        if (err.element) err.element.setAttribute('aria-invalid', 'true');
      });
      showErrorToast(messages);
    });
    document.addEventListener('input', function (e) {
      if (e.target && e.target.getAttribute && e.target.getAttribute('aria-invalid') === 'true') e.target.removeAttribute('aria-invalid');
    });
  }

  // ---- Hiện/ẩn khối theo lựa chọn radio: data-pm-show-when="TênTrường=GiáTrị" ----
  function syncConditional(form) {
    form.querySelectorAll('[data-pm-show-when]').forEach(function (block) {
      var parts = block.getAttribute('data-pm-show-when').split('=');
      var checked = form.querySelector('input[name="' + parts[0] + '"]:checked');
      var visible = checked !== null && checked.value.toLowerCase() === parts[1].toLowerCase();
      block.hidden = !visible;
      block.querySelectorAll('input, select, textarea').forEach(function (el) { el.disabled = !visible; });
    });
  }
  document.querySelectorAll('form[data-pm-conditional]').forEach(function (form) {
    form.addEventListener('change', function (e) {
      if (e.target.type === 'radio') syncConditional(form);
    });
    syncConditional(form);
  });

  // ---- Chọn nhân viên có sẵn: điền email đã lưu trong hồ sơ (nếu có) ----
  document.querySelectorAll('select[data-pm-fill-email]').forEach(function (select) {
    var target = document.getElementById(select.getAttribute('data-pm-fill-email'));
    if (!target) return;
    select.addEventListener('change', function () {
      var option = select.options[select.selectedIndex];
      var email = option ? option.getAttribute('data-email') : null;
      if (email && !target.value) target.value = email;
    });
  });

  // ---- Hộp thoại dùng chung: điền mã nhân viên, tên, email từ nút bấm ----
  document.querySelectorAll('.modal[data-pm-row-modal]').forEach(function (modal) {
    modal.addEventListener('show.bs.modal', function (e) {
      var trigger = e.relatedTarget;
      if (!trigger) return;
      modal.querySelectorAll('[data-pm-fill]').forEach(function (el) {
        var value = trigger.getAttribute('data-' + el.getAttribute('data-pm-fill')) || '';
        if (el.matches('input, textarea, select')) el.value = value;
        else el.textContent = value;
      });
    });
    modal.addEventListener('shown.bs.modal', function () {
      var first = modal.querySelector('input:not([type=hidden]), textarea');
      if (first) first.focus();
    });
  });

  // ---- Chống bấm gửi hai lần ----
  document.querySelectorAll('form').forEach(function (form) {
    form.addEventListener('submit', function () {
      if (window.jQuery && window.jQuery(form).valid && !window.jQuery(form).valid()) return;
      form.querySelectorAll('button[type=submit]').forEach(function (b) {
        window.setTimeout(function () { b.disabled = true; }, 0);
      });
    });
  });
  // Quay lại trang bằng nút Back: bật lại nút gửi.
  window.addEventListener('pageshow', function (e) {
    if (e.persisted) document.querySelectorAll('button[type=submit]').forEach(function (b) { b.disabled = false; });
  });
})();
