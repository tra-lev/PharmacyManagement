// Lọc nhanh hóa đơn của ca theo trạng thái, không tải lại trang
(function () {
  'use strict';
  var box = document.querySelector('[data-pm-shift-invoices]');
  if (!box) return;
  box.addEventListener('change', function (e) {
    if (!e.target.matches('input[name="inv-filter"]')) return;
    var value = e.target.value;
    var any = false;
    box.querySelectorAll('tr[data-pm-status]').forEach(function (row) {
      var show = value === '' || row.getAttribute('data-pm-status') === value;
      row.hidden = !show;
      if (show) any = true;
    });
    var none = box.querySelector('[data-pm-shift-none]');
    if (none) none.hidden = any;
    var table = box.querySelector('.table-responsive');
    if (table) table.hidden = !any;
  });
})();
