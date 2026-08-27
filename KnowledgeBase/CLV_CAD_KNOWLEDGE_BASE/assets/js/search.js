const searchInput = document.querySelector('#kbSearch');
const cards = Array.from(document.querySelectorAll('[data-search]'));
const count = document.querySelector('#resultCount');

function runSearch() {
  const q = (searchInput.value || '').trim().toLowerCase();
  let shown = 0;
  cards.forEach(card => {
    const haystack = card.getAttribute('data-search').toLowerCase();
    const match = !q || haystack.includes(q);
    card.classList.toggle('hidden', !match);
    if (match) shown++;
  });
  if (count) count.textContent = q ? `${shown} result${shown === 1 ? '' : 's'}` : 'Showing all sections';
}

searchInput?.addEventListener('input', runSearch);
document.querySelector('#clearSearch')?.addEventListener('click', () => { searchInput.value = ''; runSearch(); searchInput.focus(); });
runSearch();
