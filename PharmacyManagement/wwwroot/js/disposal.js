// Form biên bản hủy thuốc: chọn lô → hiện tồn và giá vốn, điền sẵn số lượng (cả lô) và lý do Hết hạn nếu lô đã hết hạn;
// tạm tính giá trị hủy từng dòng và cả biên bản. Thêm/bỏ dòng do catalog.js đảm nhận (form có data-pm-repeater-form).
// Server kiểm tra lại toàn bộ khi lưu và khi ghi sổ.
(function () {
  'use strict';
  var form = document.querySelector('[data-pm-disposal-form]');
  if (!form) return;

  var money = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 0 });
  var number = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 3 });
  var dataEl = document.getElementById('pm-disposal-batches');
  var batches = {};
  try { batches = JSON.parse(dataEl ? dataEl.textContent : '{}') || {}; } catch (e) { batches = {}; }

  function numberOf(text) {
    var n = parseFloat(text);
    return isNaN(n) ? 0 : n;
  }

  function lotOf(row) {
    var select = row.querySelector('[data-pm-lot]');
    return select ? batches[select.value] : undefined;
  }

  // Chọn lô: hiện tồn + giá vốn; số lượng trống thì lấy cả lô; lô hết hạn mà chưa chọn lý do thì chọn "Hết hạn"
  function onLotChanged(row, fill) {
    var lot = lotOf(row);
    var info = row.querySelector('[data-pm-lot-info]');
    var qty = row.querySelector('[data-pm-qty]');
    var reason = row.querySelector('[data-pm-reason]');
    if (qty) {
      if (lot) qty.max = String(lot.qty);
      else qty.removeAttribute('max');
    }
    if (info) {
      info.textContent = lot
        ? 'Tồn ' + number.format(lot.qty) + ' ' + lot.unit + ' · giá vốn ' + money.format(lot.cost) + ' đ/' + lot.unit
        : '';
    }
    if (!fill || !lot) return;
    if (qty && qty.value === '') qty.value = String(lot.qty);
    if (reason && reason.value === '' && lot.expired) reason.value = '1';
  }

  function refreshTotals() {
    var sum = 0;
    var any = false;
    form.querySelectorAll('[data-pm-disposal-line]').forEach(function (row) {
      if (row.closest('template')) return;
      var lot = lotOf(row);
      var qty = numberOf((row.querySelector('[data-pm-qty]') || {}).value);
      var out = row.querySelector('[data-pm-line-total]');
      if (!lot || qty <= 0) {
        if (out) out.textContent = '—';
        return;
      }
      var value = Math.round(qty * lot.cost);
      sum += value;
      any = true;
      if (out) out.textContent = money.format(value) + ' đ';
    });
    var total = form.querySelector('[data-pm-disposal-total]');
    if (total) total.textContent = any ? money.format(sum) + ' đ' : '—';
  }

  form.addEventListener('change', function (e) {
    var row = e.target.closest('[data-pm-disposal-line]');
    if (!row) return;
    if (e.target.matches('[data-pm-lot]')) onLotChanged(row, true);
    refreshTotals();
  });
  form.addEventListener('input', function (e) {
    if (e.target.matches('[data-pm-qty]')) refreshTotals();
  });
  // Bỏ dòng (catalog.js xử lý click trước) → tính lại tổng
  form.addEventListener('click', function (e) {
    if (e.target.closest('[data-pm-remove]')) setTimeout(refreshTotals, 0);
  });

  form.querySelectorAll('[data-pm-disposal-line]').forEach(function (row) {
    if (row.closest('template')) return;
    onLotChanged(row, false);
  });
  refreshTotals();
})();
