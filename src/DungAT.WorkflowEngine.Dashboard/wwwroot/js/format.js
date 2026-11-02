// Small formatting helpers shared by the pages.
(function () {
  const rtf = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' });
  const units = [['year', 31536000], ['month', 2592000], ['day', 86400], ['hour', 3600], ['minute', 60]];

  window.fmt = {
    ago(iso) {
      if (!iso) return '';
      const seconds = Math.round((new Date(iso).getTime() - Date.now()) / 1000);
      if (Math.abs(seconds) < 10) return 'just now';
      for (const [unit, size] of units)
        if (Math.abs(seconds) >= size) return rtf.format(Math.round(seconds / size), unit);
      return rtf.format(seconds, 'second');
    },
    dateTime(iso) {
      return iso ? new Date(iso).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' }) : '';
    },
    typeClass(type) {
      return 't-' + String(type || 'Intermediate').toLowerCase();
    },
    initials(name) {
      return String(name || '?').split(/[\s._@-]+/).filter(Boolean).slice(0, 2).map((p) => p[0].toUpperCase()).join('') || '?';
    },
    plural(n, one, many) {
      return n + ' ' + (n === 1 ? one : many || one + 's');
    },
  };
})();
