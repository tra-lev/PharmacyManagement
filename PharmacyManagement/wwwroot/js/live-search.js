// Tìm kiếm "gõ đến đâu hiện đến đó" cho các trang danh sách (Thuốc, Nhà cung cấp, Khách hàng).
// Cách dùng trong view:
//   <form method="get" data-pm-live-search>…<input type="search">…<select>…<button data-pm-live-submit>Lọc</button></form>
//   Vùng cập nhật: phần tử có data-pm-live="<tên>" (ví dụ "count", "clear", "results"); trang trả về phải có cùng tên vùng.
// Mỗi lần ngừng gõ ~300 ms (hoặc đổi ô chọn), script gọi lại CHÍNH TRANG danh sách với bộ lọc mới (server lọc và kiểm tra quyền
// như bình thường), rồi thay nội dung các vùng — không tải lại trang, con trỏ vẫn ở ô tìm kiếm.
// Không có JavaScript: nút Lọc vẫn hiện, form gửi như cũ.
(function () {
  'use strict';
  if (!window.fetch || !window.DOMParser || !window.URLSearchParams || !window.AbortController) return;

  var DELAY = 300;

  document.querySelectorAll('form[data-pm-live-search]').forEach(function (form) {
    var timer = null;
    var controller = null;
    var seq = 0;

    // Đã tự lọc khi gõ → nút Lọc không còn cần
    form.querySelectorAll('[data-pm-live-submit]').forEach(function (btn) { btn.hidden = true; });

    form.addEventListener('input', function (e) {
      if (!e.target.matches('input[type="search"], input[type="text"]')) return;
      clearTimeout(timer);
      timer = setTimeout(run, DELAY);
    });

    form.addEventListener('change', function (e) {
      if (!e.target.matches('select')) return;
      clearTimeout(timer);
      run();
    });

    // Enter trong ô tìm kiếm: lọc ngay, không gửi form
    form.addEventListener('submit', function (e) {
      e.preventDefault();
      clearTimeout(timer);
      run();
    });

    function buildUrl() {
      var params = new URLSearchParams();
      new FormData(form).forEach(function (value, key) {
        var text = String(value).trim();
        if (text !== '') params.append(key, text);
      });
      // Đổi bộ lọc thì về trang 1 (form không có ô Page nên tự động bỏ)
      var action = form.getAttribute('action') || window.location.pathname;
      var query = params.toString();
      return action + (query ? '?' + query : '');
    }

    function regions() {
      return document.querySelectorAll('[data-pm-live]');
    }

    function setBusy(busy) {
      regions().forEach(function (el) {
        if (busy) el.setAttribute('aria-busy', 'true');
        else el.removeAttribute('aria-busy');
      });
    }

    function run() {
      var url = buildUrl();
      if (controller) controller.abort();
      controller = new AbortController();
      var mine = ++seq;
      setBusy(true);

      fetch(url, {
        credentials: 'same-origin',
        headers: { 'X-Requested-With': 'fetch' },
        signal: controller.signal
      })
        .then(function (res) {
          if (!res.ok) throw new Error('HTTP ' + res.status);
          return res.text();
        })
        .then(function (html) {
          if (mine !== seq) return;
          var doc = new DOMParser().parseFromString(html, 'text/html');
          var current = regions();
          var complete = current.length > 0;
          current.forEach(function (el) {
            if (!doc.querySelector('[data-pm-live="' + el.getAttribute('data-pm-live') + '"]')) complete = false;
          });
          // Trang trả về không đúng dạng (ví dụ hết phiên đăng nhập → trang đăng nhập): chuyển trang bình thường
          if (!complete) {
            window.location.href = url;
            return;
          }
          current.forEach(function (el) {
            var fresh = doc.querySelector('[data-pm-live="' + el.getAttribute('data-pm-live') + '"]');
            el.innerHTML = fresh.innerHTML;
          });
          window.history.replaceState(null, '', url);
          setBusy(false);
        })
        .catch(function (err) {
          if (err && err.name === 'AbortError') return;
          // Lỗi mạng / lỗi máy chủ: chuyển trang bình thường để người dùng thấy thông báo lỗi của hệ thống
          window.location.href = url;
        });
    }
  });
})();
