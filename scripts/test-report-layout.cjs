// Requires Playwright and Chromium. Start the app locally before running:
// node scripts/test-report-layout.cjs http://127.0.0.1:5080/api/v1/exports/report.html
// Only reads the endpoint. Stress fixtures are applied to the browser DOM.
const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');

(async () => {
  const output = path.resolve('work/report-layout');
  await fs.mkdir(output, { recursive: true });
  const browser = await chromium.launch({ headless: true });
  try {
    const context = await browser.newContext({ isMobile: true, hasTouch: true, deviceScaleFactor: 2 });
    const page = await context.newPage();
    const response = await page.goto(process.argv[2] || 'http://127.0.0.1:5080/api/v1/exports/report.html');
    assert.equal(response.status(), 200);
    assert.match(response.headers()['cache-control'] || '', /no-store/);
    const html = await response.text();
    let checks = 0;
    for (const fixture of ['original', 'empty', 'long']) {
      await page.setContent(html);
      if (fixture === 'empty') {
        await page.locator('tbody').evaluateAll(bodies => bodies.forEach(body => {
          body.innerHTML = '<tr><td colspan="4" class="empty">Nenhum registro neste período.</td></tr>';
        }));
      }
      if (fixture === 'long') {
        await page.locator('table').evaluateAll(tables => tables.forEach((table, index) => {
          const body = table.querySelector('tbody');
          body.replaceChildren();
          for (let n = 0; n < 30; n++) {
            const row = body.insertRow();
            const values = index === 0
              ? ['Canal' + 'X'.repeat(120), 'R$ 1.234.567,89', 'R$ 987.654,32', '10 de 15']
              : ['03/11/2026', 'Recebimento · estorno', `Pedido ${n + 1} ` + 'Encomenda'.repeat(24), '-R$ 1.234.567,89'];
            const labels = index === 0
              ? ['Canal', 'Vendas líquidas', 'Margem conhecida', 'Vendas com custo']
              : ['Data', 'Tipo', 'Descrição', 'Valor'];
            values.forEach((value, i) => {
              const cell = row.insertCell();
              cell.textContent = value;
              cell.dataset.label = labels[i];
              if (value.includes('R$')) cell.className = 'amount';
            });
          }
        }));
      }
      for (const width of [320, 360, 390, 412, 640, 641, 760, 761, 980, 1280]) {
        await page.setViewportSize({ width, height: 900 });
        await page.emulateMedia({ media: 'screen' });
        const overflow = await page.evaluate(() => {
          const sheet = document.querySelector('.sheet');
          const style = getComputedStyle(sheet);
          const right = sheet.getBoundingClientRect().right - parseFloat(style.paddingRight);
          return {
            page: document.documentElement.scrollWidth > innerWidth + 1,
            cells: [...document.querySelectorAll('tbody td')].some(cell =>
              cell.getBoundingClientRect().right > right + 1 || cell.scrollWidth > cell.clientWidth + 1)
          };
        });
        assert.deepEqual(overflow, { page: false, cells: false }, `${fixture}, width ${width}`);
        checks++;
      }
      await page.setViewportSize({ width: 390, height: 844 });
      await page.screenshot({ path: path.join(output, `${fixture}-mobile.png`), fullPage: fixture !== 'long' });
      await page.emulateMedia({ media: 'print' });
      assert.equal(await page.locator('.toolbar').evaluate(el => getComputedStyle(el).display), 'none');
      assert.equal(await page.locator('thead').first().evaluate(el => getComputedStyle(el).display), 'table-header-group');
      assert.equal(await page.locator('tbody td').first().evaluate(el => getComputedStyle(el).display), 'table-cell');
      await page.pdf({ path: path.join(output, `${fixture}.pdf`), preferCSSPageSize: true, printBackground: true });
    }
    console.log(`${checks} layout checks passed; mobile screenshots and A4 PDFs: ${output}`);
  } finally {
    await browser.close();
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
