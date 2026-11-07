// Nhập số thực tế kiểm kê: gõ số đếm được → hiện chênh lệch so với tồn lúc tạo phiếu, đánh dấu ô lý do khi có chênh lệch,
// đếm số lô đã nhập / chênh lệch. Chỉ hỗ trợ nhập liệu; server kiểm tra lại toàn bộ khi nộp biên bản.
(function () {
  'use strict';
  var form = document.querySelector('[data-pm-stocktake-form]');
  if (!form) return;

  var number = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 3 });

  function refreshRow(row) {
    var system = parseFloat(row.getAttribute('data-pm-system') || '0');
    var input = row.querySelector('[data-pm-actual]');
    var out = row.querySelector('[data-pm-diff]');
    var reason = row.querySelector('[data-pm-reason]');
    var text = input ? String(input.value).trim() : '';
    var actual = parseFloat(text);
    if (text === '' || isNaN(actual)) {
      if (out) { out.textContent = '—'; out.classList.remove('fw-semibold', 'text-danger'); }
      if (reason) reason.removeAttribute('aria-invalid');
      return { entered: false, diff: false };
    }
    var diff = Math.round((actual - system) * 1000) / 1000;
    if (out) {
      out.textContent = diff === 0 ? 'Khớp' : (diff > 0 ? '+' : '−') + number.format(Math.abs(diff));
      out.classList.toggle('fw-semibold', diff !== 0);
      out.classList.toggle('text-danger', diff < 0);
    }
    if (reason) {
      if (diff !== 0 && String(reason.value).trim() === '') reason.setAttribute('aria-invalid', 'true');
      else reason.removeAttribute('aria-invalid');
    }
    return { entered: true, diff: diff !== 0 };
  }

  function refreshAll() {
    var entered = 0;
    var diffs = 0;
    form.querySelectorAll('[data-pm-count-line]').forEach(function (row) {
      var r = refreshRow(row);
      if (r.entered) entered++;
      if (r.diff) diffs++;
    });
    var e = form.querySelector('[data-pm-entered]');
    var d = form.querySelector('[data-pm-diffs]');
    if (e) e.textContent = number.format(entered);
    if (d) d.textContent = number.format(diffs);
  }

  form.addEventListener('input', function (e) {
    if (e.target.matches('[data-pm-actual], [data-pm-reason]')) refreshAll();
  });
  form.addEventListener('change', function (e) {
    if (e.target.matches('[data-pm-actual], [data-pm-reason]')) refreshAll();
  });
  refreshAll();
})();
