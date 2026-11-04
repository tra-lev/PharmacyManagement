// Đổi trả (3.4.2–3.4.3): tính TẠM tiền hoàn để quản lý thấy ngay; server tính lại khi lưu.
// Tiền hoàn mỗi dòng = số trả × tiền hoàn mỗi đơn vị; trả hết phần còn lại thì hoàn đúng số tiền còn lại của dòng.
// Hình thức hoàn: tiền mặt (cần ca đang mở) / chuyển khoản / thẻ (cần mã giao dịch).
(function () {
  'use strict';
  var form = document.querySelector('[data-pm-return-form]');
  if (!form) return;
  var money = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 0 });
  var hasShift = form.getAttribute('data-pm-has-shift') === 'true';

  function num(el, name) {
    var v = parseFloat(el.getAttribute(name) || '0');
    return isNaN(v) ? 0 : v;
  }

  function refreshTotal() {
    var total = 0;
    form.querySelectorAll('[data-pm-return-line]').forEach(function (row) {
      var input = row.querySelector('[data-pm-return-qty]');
      if (!input) return;
      var qty = parseInt(input.value, 10);
      var remaining = num(row, 'data-remaining');
      if (!(qty > 0)) return;
      if (qty > remaining) qty = remaining;
      total += qty === remaining ? num(row, 'data-rest') : qty * num(row, 'data-unit');
    });
    var out = form.querySelector('[data-pm-refund-total]');
    if (out) out.textContent = money.format(Math.round(total)) + ' đ';
  }

  function refreshMethod() {
    var checked = form.querySelector('input[name="RefundMethod"]:checked');
    var method = checked ? checked.value : '1';
    var ref = form.querySelector('[data-pm-refund-ref]');
    if (ref) {
      ref.hidden = method === '1';
      ref.querySelectorAll('input').forEach(function (i) { i.disabled = method === '1'; });
    }
    var noShift = form.querySelector('[data-pm-cash-noshift]');
    if (noShift) noShift.hidden = !(method === '1' && !hasShift);
  }

  form.addEventListener('input', function (e) {
    if (e.target.matches('[data-pm-return-qty]')) refreshTotal();
  });
  form.addEventListener('change', function (e) {
    if (e.target.matches('input[name="RefundMethod"]')) refreshMethod();
    if (e.target.matches('[data-pm-return-qty]')) refreshTotal();
  });
  refreshMethod();
  refreshTotal();
})();
