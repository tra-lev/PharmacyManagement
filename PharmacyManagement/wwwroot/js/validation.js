// Kiểm tra form NGAY KHI NGƯỜI DÙNG GÕ (jQuery Validation + Unobtrusive của template MVC).
// Chỉ để báo sớm; server vẫn kiểm tra lại toàn bộ. Nạp SAU jquery.validate và jquery.validate.unobtrusive
// (partial _FormValidation), TRƯỚC khi trang tải xong để cấu hình kịp áp dụng.
(function ($) {
  'use strict';
  if (!$ || !$.validator || !$.validator.unobtrusive) return;

  // ---- Quy tắc mật khẩu: khớp Models/Validation/PasswordPolicyAttribute.cs và cấu hình Identity ----
  function missingParts(value, min) {
    var missing = [];
    if (value.length < min) missing.push('ít nhất ' + min + ' ký tự');
    if (!/[0-9]/.test(value)) missing.push('chữ số (0–9)');
    if (!/[a-z]/.test(value)) missing.push('chữ thường (a–z)');
    if (!/[A-Z]/.test(value)) missing.push('chữ hoa (A–Z)');
    return missing;
  }

  $.validator.addMethod('pmpassword', function (value, element, min) {
    return this.optional(element) || missingParts(value, min).length === 0;
  }, function (min, element) {
    return 'Mật khẩu còn thiếu: ' + missingParts(element.value, min).join(', ') + '.';
  });

  // Không gán message cố định để câu báo lỗi liệt kê đúng phần còn thiếu.
  $.validator.unobtrusive.adapters.add('pmpassword', ['min'], function (options) {
    options.rules.pmpassword = parseInt(options.params.min, 10) || 8;
  });

  // ---- Báo lỗi ngay khi gõ (mặc định thư viện chỉ báo sau lần bấm gửi đầu tiên) ----
  // Ô có data-pm-lazy (ô mã 6 số) giữ cách cũ: báo khi rời ô hoặc khi gửi, để không đỏ ngay từ chữ số đầu.
  var ignoredKeys = [16, 17, 18, 20, 35, 36, 37, 38, 39, 40, 45, 144, 225];
  $.validator.setDefaults({
    onkeyup: function (element, event) {
      if (event.which === 9 && this.elementValue(element) === '') return;
      if ($.inArray(event.keyCode, ignoredKeys) !== -1) return;
      var lazy = element.hasAttribute('data-pm-lazy');
      if (!lazy || element.name in this.submitted || element.name in this.invalid) {
        this.element(element);
      }
    }
  });
})(window.jQuery);
