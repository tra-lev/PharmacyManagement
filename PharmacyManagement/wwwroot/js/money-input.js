// Ô nhập tiền (input có data-pm-money): gõ đến đâu tự thêm dấu chấm hàng nghìn đến đó — 200000 → 200.000.
// Chỉ giữ chữ số; con trỏ giữ đúng vị trí khi gõ / xóa giữa chừng. Dán "200,000" hay "200.000đ" đều thành 200.000.
// Server đọc lại bằng Infrastructure/MoneyModelBinder.cs (bỏ dấu chấm), nên không cần JavaScript vẫn gửi đúng.
// Dùng chung cho mọi trang (nạp ở _Scripts.cshtml); ô thêm động (dòng quy đổi) cũng được xử lý.
(function () {
  'use strict';
  var MAX_DIGITS = 15;
  var formatter = window.Intl ? new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 0 }) : null;

  function digitsOf(text) {
    return String(text || '').replace(/\D/g, '');
  }

  function group(digits) {
    if (digits === '') return '';
    if (formatter) return formatter.format(Number(digits));
    return digits.replace(/\B(?=(\d{3})+(?!\d))/g, '.');
  }

  function format(input) {
    var old = input.value;
    var caret = typeof input.selectionStart === 'number' ? input.selectionStart : old.length;
    var digitsBeforeCaret = digitsOf(old.slice(0, caret)).length;

    var digits = digitsOf(old).replace(/^0+(?=\d)/, '');
    if (digits.length > MAX_DIGITS) digits = digits.slice(0, MAX_DIGITS);
    var next = group(digits);
    if (next === old) return;
    input.value = next;

    if (document.activeElement !== input) return;
    // Đặt con trỏ sau đúng số chữ số đã có trước nó
    var pos = 0;
    var seen = 0;
    while (pos < next.length && seen < digitsBeforeCaret) {
      if (next.charAt(pos) >= '0' && next.charAt(pos) <= '9') seen++;
      pos++;
    }
    input.setSelectionRange(pos, pos);
  }

  document.addEventListener('input', function (e) {
    var t = e.target;
    if (t && t.matches && t.matches('input[data-pm-money]')) format(t);
  });

  document.querySelectorAll('input[data-pm-money]').forEach(format);
})();
