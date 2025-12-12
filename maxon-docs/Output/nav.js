// Navigation data and dynamic sidebar generation
const categories = {
    'collections': 'Collections',
    'compilation': 'Compilation',
    'control-flow': 'Control Flow',
    'core': 'Core',
    'declaration': 'Declarations',
    'diagnostics': 'Diagnostics',
    'error-handling': 'Error Handling',
    'expressions': 'Expressions',
    'functions': 'Functions',
    'internals': 'Internals',
    'interop': 'Interoperability',
    'literals': 'Literals',
    'math-intrinsic': 'Math Intrinsics',
    'namespaces': 'Namespaces',
    'operators': 'Operators',
    'optimization': 'Optimization',
    'organization': 'Organization',
    'statements': 'Statements',
    'stdlib': 'Standard Library',
    'strings': 'Strings',
    'type-system': 'Type System',
    'types': 'Types',
    'unicode': 'Unicode'
};

// Build and insert sidebar navigation
document.addEventListener('DOMContentLoaded', function() {
    const sidebar = document.getElementById('sidebar');
    if (!sidebar) return;

    const activeCategory = sidebar.dataset.active;

    let html = '<h2>Maxon Docs</h2><ul>';
    html += '<li><a href="index.html">← Home</a></li>';

    for (const [key, displayName] of Object.entries(categories)) {
        const activeClass = key === activeCategory ? ' class="active"' : '';
        html += `<li><a href="${key}.html"${activeClass}>${displayName}</a></li>`;
    }

    html += '</ul>';
    sidebar.innerHTML = html;
});
