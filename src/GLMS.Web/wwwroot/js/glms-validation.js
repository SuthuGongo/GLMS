(function ($) {
    if (!$ || !$.validator || !$.validator.unobtrusive) return;

    function otherField(element, name) {
        // Fields in the same form, matched by name (works for nested prefixes too).
        var form = $(element).closest('form');
        return form.find('[name="' + name + '"], [name$=".' + name + '"]').first();
    }

    // End date must be after start date
    $.validator.addMethod('dateafter', function (value, element, other) {
        if (!value) return true;
        var otherValue = otherField(element, other).val();
        if (!otherValue) return true;
        return new Date(value) > new Date(otherValue);
    });
    $.validator.unobtrusive.adapters.add('dateafter', ['other'], function (options) {
        options.rules.dateafter = options.params.other;
        options.messages.dateafter = options.message;
    });

    // At most N decimal places
    $.validator.addMethod('maxdecimals', function (value, element, places) {
        if (!value) return true;
        var parts = String(value).trim().split('.');
        return parts.length < 2 || parts[1].length <= Number(places);
    });
    $.validator.unobtrusive.adapters.add('maxdecimals', ['places'], function (options) {
        options.rules.maxdecimals = options.params.places;
        options.messages.maxdecimals = options.message;
    });

    // PDF only, not empty, within the size limit
    $.validator.addMethod('pdffile', function (value, element, maxBytes) {
        var file = element.files && element.files[0];
        if (!file) return true;
        return /\.pdf$/i.test(file.name) && file.size > 0 && file.size <= Number(maxBytes);
    });
    $.validator.unobtrusive.adapters.add('pdffile', ['maxbytes'], function (options) {
        options.rules.pdffile = options.params.maxbytes;
        options.messages.pdffile = options.message;
    });

    // Required only when a checkbox is ticked
    $.validator.addMethod('requirediftrue', function (value, element, other) {
        var box = otherField(element, other).filter('[type=checkbox]');
        if (!box.length || !box.is(':checked')) return true;
        return element.type === 'file' ? !!(element.files && element.files.length) : $.trim(value).length > 0;
    });
    $.validator.unobtrusive.adapters.add('requirediftrue', ['other'], function (options) {
        options.rules.requirediftrue = options.params.other;
        options.messages.requirediftrue = options.message;
    });

    // Re-check the dependent field when the field it depends on changes.
    $(document).on('change', 'input[type=checkbox], input[type=date]', function () {
        var form = $(this).closest('form');
        if (!form.data('validator')) return;
        form.find('[data-val-requirediftrue-other="' + this.name + '"], [data-val-dateafter-other="' + this.name + '"]').each(function () {
            $(this).valid();
        });
    });
})(window.jQuery);
