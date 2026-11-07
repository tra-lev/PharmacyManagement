// Trang phân quyền: ô "Chọn cả nhóm" bật / tắt mọi quyền chưa bị khóa trong nhóm, và đếm số quyền đang chọn.
// Server kiểm tra lại toàn bộ khi lưu (quyền bị khóa luôn bị bỏ qua).
(function () {
  'use strict';
  var form = document.querySelector('[data-pm-roles]');
  if (!form) return;
  var counter = form.querySelector('[data-pm-role-count]');

  function items(group) {
    return Array.prototype.slice.call(group.querySelectorAll('[data-pm-role-item]'));
  }

  function syncGroup(group) {
    var all = group.querySelector('[data-pm-role-all]');
    if (!all) return;
    var enabled = items(group).filter(function (i) { return !i.disabled; });
    var checked = enabled.filter(function (i) { return i.checked; }).length;
    all.checked = enabled.length > 0 && checked === enabled.length;
    all.indeterminate = checked > 0 && checked < enabled.length;
  }

  function count() {
    if (!counter) return;
    counter.textContent = String(form.querySelectorAll('[data-pm-role-item]:checked').length);
  }

  form.addEventListener('change', function (e) {
    var group = e.target.closest('[data-pm-role-group]');
    if (!group) return;
    if (e.target.matches('[data-pm-role-all]')) {
      items(group).forEach(function (i) { if (!i.disabled) i.checked = e.target.checked; });
    }
    syncGroup(group);
    count();
  });

  form.querySelectorAll('[data-pm-role-group]').forEach(syncGroup);
  count();
})();
