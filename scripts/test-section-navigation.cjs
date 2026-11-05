// Requires Playwright/Chromium and a running local application. No API writes.
// node scripts/test-section-navigation.cjs [http://127.0.0.1:5080]
const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');

(async () => {
    const base = process.argv[2] || 'http://127.0.0.1:5080';
    const output = path.resolve('work/section-navigation');
    await fs.mkdir(output, { recursive: true });
    const browser = await chromium.launch();
    try {
        for (const width of [1280, 390]) {
            const page = await browser.newPage({ viewport: { width, height: 900 }, isMobile: width === 390 });
            const errors = [];
            page.on('pageerror', error => errors.push(error.message));
            const ids = ['dashboard-filters-option', 'dashboard-metrics-option', 'dashboard-notices-option',
                'dashboard-channel-option', 'dashboard-ledger-option'];
            const ready = async () => page.waitForFunction(() => document.getElementById('page-loader').hidden);
            const opened = id => page.locator(`#${id}`).evaluate(el => el.open);
            const assertClosed = async id => {
                assert.equal(await opened(id), false, `${width}px ${id} should be closed`);
                assert.equal(await page.locator(`#${id}`).boundingBox(), null, `${width}px ${id} should leave no visible wrapper`);
                assert.equal(await page.locator(`[data-expand-target="${id}"]`).getAttribute('aria-expanded'), 'false');
            };
            const showOptions = async () => {
                if (width === 390 && await page.locator('#mobile-menu-toggle').getAttribute('aria-expanded') !== 'true')
                    await page.locator('#mobile-menu-toggle').click();
                const toggle = page.locator('.nav-entry-toggle');
                if (await toggle.getAttribute('aria-expanded') !== 'true') await toggle.click();
            };
            const option = async id => {
                const before = await opened(id);
                await showOptions();
                await page.locator(`[data-expand-target="${id}"]`).click();
                await page.waitForFunction(({ id, before }) => {
                    const section = document.getElementById(id);
                    const link = document.querySelector(`[data-expand-target="${id}"]`);
                    return section.open === !before && link.getAttribute('aria-expanded') === String(!before);
                }, { id, before });
                // Allow the navigation panel collapse and smooth scrolling to settle.
                await page.waitForTimeout(400);
            };
            const assertOrder = async () => {
                const result = await page.evaluate(ids => {
                    const charts = document.querySelector('.dashboard-charts').getBoundingClientRect();
                    return { baseBottom: charts.bottom + scrollY, sections: ids.map(id => {
                        const el = document.getElementById(id), box = el.getBoundingClientRect();
                        return { id, open: el.open, top: box.top + scrollY, bottom: box.bottom + scrollY, height: box.height };
                    }) };
                }, ids);
                let end = result.baseBottom;
                for (const section of result.sections.filter(section => section.open)) {
                    assert.ok(section.height > 0, `${section.id} must have a real layout box`);
                    assert.ok(section.top >= end - 1, `${width}px ${section.id} must follow the preceding section`);
                    end = section.bottom;
                }
            };
            const assertNoticeInView = async () => {
                await page.waitForFunction(() => {
                    const box = document.getElementById('dashboard-notices-option').getBoundingClientRect();
                    const heading = document.querySelector('#dashboard-alerts h2').getBoundingClientRect();
                    return box.width > 0 && box.height > 0 && heading.top >= 0 && heading.bottom <= innerHeight;
                });
                assert.equal(await opened('dashboard-notices-option'), true);
            };
            const assertNoOverflow = async () => assert.ok(await page.evaluate(() =>
                document.documentElement.scrollWidth <= innerWidth + 1), `${width}px horizontal overflow`);

            await page.goto(base); await ready();
            for (const id of ids) await assertClosed(id);
            await assertNoOverflow();
            await page.screenshot({ path: path.join(output, `initial-${width}.png`), fullPage: true });

            // Reverse opening order must never change the order defined in the menu.
            for (const id of [...ids].reverse()) { await option(id); await assertOrder(); }
            await assertNoOverflow();
            // Hover must not replace the selected option's active colors/shadow.
            await showOptions();
            const selected = page.locator('[data-expand-target="dashboard-filters-option"]');
            const selectedStyle = () => selected.evaluate(el => {
                const css = getComputedStyle(el);
                return { background: css.backgroundColor, boxShadow: css.boxShadow };
            });
            await page.mouse.move(width - 4, 895); await page.waitForTimeout(250);
            const normalStyle = await selectedStyle();
            await selected.hover(); await page.waitForTimeout(250);
            assert.deepEqual(await selectedStyle(), normalStyle, `${width}px active option style changed on hover`);
            await page.screenshot({ path: path.join(output, `selected-option-hover-${width}.png`) });
            await option('dashboard-notices-option'); await assertClosed('dashboard-notices-option');
            await option('dashboard-notices-option'); await assertNoticeInView(); await assertOrder();
            await page.screenshot({ path: path.join(output, `notices-all-open-${width}.png`) });

            // Closing and reopening one section preserves all others and original order.
            await option('dashboard-metrics-option'); await assertClosed('dashboard-metrics-option');
            await option('dashboard-channel-option'); await assertClosed('dashboard-channel-option');
            assert.equal(await opened('dashboard-ledger-option'), true);
            await option('dashboard-notices-option'); await option('dashboard-notices-option');
            await assertNoticeInView(); await assertOrder();
            await page.screenshot({ path: path.join(output, `notices-some-open-${width}.png`) });
            await option('dashboard-metrics-option'); await assertOrder();
            await page.locator('#dashboard-metrics-option > summary').click();
            await page.waitForFunction(() => document.querySelector('[data-expand-target="dashboard-metrics-option"]').getAttribute('aria-expanded') === 'false');
            await assertClosed('dashboard-metrics-option');
            await assertNoOverflow();

            // Avisos remains navigable when there are no financial warnings.
            await page.route('**/api/v1/dashboard?*', async route => {
                const response = await route.fetch();
                const summary = await response.json();
                summary.warnings = []; summary.salesWithCost = summary.salesCount;
                await route.fulfill({ response, json: summary });
            });
            await page.goto(base); await ready();
            await option('dashboard-notices-option'); await assertNoticeInView();
            assert.ok((await page.locator('#dashboard-notices').innerText()).trim().length > 0, 'Empty state must be explained');
            await assertNoOverflow();

            // Shared section controls must keep unsaved form inputs in the DOM.
            await page.goto(`${base}/Movements`); await ready();
            await option('expense-option');
            await page.locator('#expense-form [name="description"]').fill('Rascunho preservado');
            await option('expense-option'); await assertClosed('expense-option');
            await option('owner-option'); await option('expense-option');
            assert.equal(await page.locator('#expense-form [name="description"]').inputValue(), 'Rascunho preservado');
            assert.equal(await opened('owner-option'), true);
            await assertNoOverflow();

            // Recipe tools stay below the initial workflow and in menu order.
            await page.goto(`${base}/Recipes`); await ready();
            await assertClosed('purchase-import-option'); await assertClosed('saved-recipes-option');
            const workflowTop = await page.locator('#recipe-workflow').evaluate(el => el.getBoundingClientRect().top + scrollY);
            const assertRecipeOrder = async () => {
                const positions = await page.evaluate(() => {
                    const rect = id => {
                        const box = document.getElementById(id).getBoundingClientRect();
                        return { top: box.top + scrollY, bottom: box.bottom + scrollY };
                    };
                    return { workflow: rect('recipe-workflow'), imports: rect('purchase-import-option'), saved: rect('saved-recipes-option') };
                });
                assert.ok(Math.abs(positions.workflow.top - workflowTop) < 1, 'Opening recipe tools moved the initial workflow');
                assert.ok(positions.imports.top >= positions.workflow.bottom - 1, 'Import must follow the initial recipe workflow');
                assert.ok(positions.saved.top >= positions.imports.bottom - 1, 'Saved recipes must follow import regardless of click order');
            };
            await option('saved-recipes-option'); await option('purchase-import-option');
            await assertRecipeOrder(); await assertNoOverflow();
            await option('purchase-import-option'); await assertClosed('purchase-import-option');
            assert.equal(await opened('saved-recipes-option'), true);
            await option('purchase-import-option'); await assertRecipeOrder();
            await page.screenshot({ path: path.join(output, `recipes-options-${width}.png`) });
            assert.deepEqual(errors, [], `${width}px browser errors`);
            console.log(`${width}px: hierarchy, independent toggles, notices scrolling, active hover style, empty state, drafts and recipe options OK`);
            await page.close();
        }
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
