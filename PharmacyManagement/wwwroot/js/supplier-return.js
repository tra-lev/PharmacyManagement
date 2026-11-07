// Form phiếu trả nhà cung cấp: chọn lô → hiện tồn và giá vốn, điền sẵn số lượng (cả lô) và đơn giá (giá vốn) nếu đang trống;
// tạm tính thành tiền từng dòng và tổng tiền hoàn dự kiến. Thêm/bỏ dòng do catalog.js đảm nhận (form có data-pm-repeater-form).
// Server kiểm tra lại toàn bộ khi lưu và khi xuất trả.
(function () {
  'use strict';
  var form = document.querySelector('[data-pm-sret-form]');
  if (!form) return;

  var money = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 0 });
  var number = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 3 });
  var dataEl = document.getElementById('pm-sret-batches');
  var batches = {};
  try { batches = JSON.parse(dataEl ? dataEl.textContent : '{}') || {}; } catch (e) { batches = {}; }

  function numberOf(text) {
    var n = parseFloat(text);
    return isNaN(n) ? NaN : n;
  }

  function lotOf(row) {
    var select = row.querySelector('[data-pm-lot]');
    return select ? batches[select.value] : undefined;
  }

  function onLotChanged(row, fill) {
    var lot = lotOf(row);
    var info = row.querySelector('[data-pm-lot-info]');
    var qty = row.querySelector('[data-pm-qty]');
    var cost = row.querySelector('[data-pm-cost]');
    if (qty) {
      if (lot) qty.max = String(lot.qty);
      else qty.removeAttribute('max');
    }
    if (info) {
      info.textContent = lot
        ? 'Tồn ' + number.format(lot.qty) + ' ' + lot.unit + ' · giá vốn ' + number.format(lot.cost) + ' đ/' + lot.unit
        : '';
    }
    if (!fill || !lot) return;
    if (qty && qty.value === '') qty.value = String(lot.qty);
    if (cost && cost.value === '') cost.value = String(lot.cost);
  }

  function refreshTotals() {
    var sum = 0;
    var any = false;
    form.querySelectorAll('[data-pm-sret-line]').forEach(function (row) {
      if (row.closest('template')) return;
      var lot = lotOf(row);
      var qty = numberOf((row.querySelector('[data-pm-qty]') || {}).value);
      var costText = (row.querySelector('[data-pm-cost]') || {}).value;
      var cost = costText === '' || costText === undefined ? (lot ? lot.cost : NaN) : numberOf(costText);
      var out = row.querySelector('[data-pm-line-total]');
      if (!lot || isNaN(qty) || qty <= 0 || isNaN(cost)) {
        if (out) out.textContent = '—';
        return;
      }
      var value = Math.round(qty * cost);
      sum += value;
      any = true;
      if (out) out.textContent = money.format(value) + ' đ';
    });
    var total = form.querySelector('[data-pm-sret-total]');
    if (total) total.textContent = any ? money.format(sum) + ' đ' : '—';
  }

  form.addEventListener('change', function (e) {
    var row = e.target.closest('[data-pm-sret-line]');
    if (!row) return;
    if (e.target.matches('[data-pm-lot]')) onLotChanged(row, true);
    refreshTotals();
  });
  form.addEventListener('input', function (e) {
    if (e.target.matches('[data-pm-qty], [data-pm-cost]')) refreshTotals();
  });
  form.addEventListener('click', function (e) {
    if (e.target.closest('[data-pm-remove]')) setTimeout(refreshTotals, 0);
  });

  form.querySelectorAll('[data-pm-sret-line]').forEach(function (row) {
    if (row.closest('template')) return;
    onLotChanged(row, false);
  });
  refreshTotals();
})();
