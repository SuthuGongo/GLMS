// GLMS client-side helpers.

// Client-side PDF check for any <input type="file" data-pdf-only>. The server re-validates
// (extension AND file signature), so this is only for fast feedback.
document.addEventListener('change', function (e) {
    const input = e.target;
    if (!(input instanceof HTMLInputElement) || input.type !== 'file' || !input.hasAttribute('data-pdf-only')) return;

    const feedback = document.getElementById(input.dataset.feedback || '');
    const file = input.files && input.files[0];
    let message = '';
    if (file) {
        if (!file.name.toLowerCase().endsWith('.pdf')) message = 'Only PDF files are allowed.';
        else if (file.size === 0) message = 'The selected file is empty.';
        else if (file.size > 10 * 1024 * 1024) message = 'The file is larger than 10 MB.';
    }

    input.setCustomValidity(message);
    input.classList.toggle('is-invalid', message !== '');
    if (feedback) feedback.textContent = message;
});

// ---------------------------------------------------------------------------------------------
// Exact money maths for the live ZAR preview.
// JavaScript numbers are binary floating point (0.1 + 0.2 !== 0.3), so the preview works in whole
// integers with BigInt instead: cents x rate-in-millionths, then rounds half away from zero to cents.
// This gives exactly the same result as the server's CurrencyCalculator (decimal, AwayFromZero).
// ---------------------------------------------------------------------------------------------
window.glmsMoney = (function () {
    function toScaledBigInt(text, decimals) {
        var t = String(text == null ? '' : text).trim();
        if (!/^\d+(\.\d+)?$/.test(t)) return null;
        var parts = t.split('.');
        var frac = (parts[1] || '');
        if (frac.length > decimals) return null;             // more precision than allowed
        return BigInt(parts[0] + frac.padEnd(decimals, '0'));
    }

    /** Converts e.g. ("1250.75", "18.4523") to "23079.63" (as a plain string, 2 decimals). Returns null for bad input. */
    function convert(amountText, rateText) {
        var cents = toScaledBigInt(amountText, 2);           // amount x 100
        var rate = toScaledBigInt(rateText, 6);              // rate x 1,000,000
        if (cents === null || rate === null || rate === 0n) return null;
        var product = cents * rate;                          // result x 100,000,000
        var resultCents = (product + 500000n) / 1000000n;    // round half away from zero (all values are positive)
        var whole = resultCents / 100n, fraction = resultCents % 100n;
        return whole.toString() + '.' + fraction.toString().padStart(2, '0');
    }

    function format(plain) {
        if (plain === null) return 'R 0.00';
        var parts = plain.split('.');
        return 'R ' + parts[0].replace(/\B(?=(\d{3})+(?!\d))/g, ',') + '.' + parts[1];
    }

    return { convert: convert, format: format };
})();
