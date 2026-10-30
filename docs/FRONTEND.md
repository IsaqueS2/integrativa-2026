# Integração do front-end

Base local `http://127.0.0.1:5080/api/v1`. Contrato completo em `/docs`. Sem autenticação nesta versão local.

## Convenções

JSON camelCase. Dinheiro em centavos inteiros (`10000` = R$ 100,00); percentuais em pontos-base (`1000` = 10%). Datas `YYYY-MM-DD`, sem horário. Quantidades de receitas são números decimais.

Listas paginadas: `{ "items": [], "total": 0, "page": 1, "pageSize": 25 }`. Vendas/despesas/extrato/proprietária: padrão 25. Ingredientes/receitas: padrão 100. Limites `page >= 1`, `pageSize` entre 1 e 100. `GET /catalogs` retorna `{categories,channels,paymentMethods}` com arrays.

Criação retorna `201` e entidade com `id`. Atualizações/ações retornam `200`. Erros usam Problem Details com `detail`: `400` para entrada inválida, `404` para referência ausente, `409` para duplicação/confirmação conflitante. Não dispare mutações por GET.

## Rotas

| Recurso | Métodos e caminhos |
|---|---|
| Configuração | `GET`, `PUT /settings` |
| Cadastros | `GET /catalogs`; `POST /categories`, `/channels`, `/payment-methods`; `GET`, `PUT` com `/{id}` |
| Vendas | `GET`, `POST /sales`; `GET /sales/{id}` |
| Receber/cancelar | `POST /sales/{id}/receive`, `/cancel` |
| Despesas | `GET`, `POST /expenses`; `GET /expenses/{id}`; `POST /expenses/{id}/pay`, `/cancel` |
| Proprietária | `GET`, `POST /owner-movements`; `GET /owner-movements/{id}`; `POST /owner-movements/{id}/cancel` |
| Consultas | `GET /dashboard`, `/ledger`, `/daily-summary?date=2026-10-26` |
| Exportações | `GET /exports/ledger.csv`, `/exports/summary.csv` (CSV); `GET /exports/report.html` (relatório colorido para imprimir ou salvar como PDF) |
| Ingredientes | `GET`, `POST /ingredients`; `GET`, `PUT /ingredients/{id}` |
| Receitas | `GET`, `POST /recipes`; `GET`, `PUT /recipes/{id}` |
| Custo e preço | `GET /recipes/{id}/cost`; `POST /recipes/{id}/price` |

Filtros financeiros: `from`, `to`, `channelId`, `paymentMethodId`, `categoryId`. Vendas/despesas também aceitam `status`. Paginação: `page`, `pageSize`. As exportações preservam os filtros e ignoram paginação. O relatório HTML inclui resumo, vendas por canal e movimentações; use “Imprimir / Salvar em PDF” e escolha “Salvar como PDF” no navegador.

## Configuração e categorias

`PUT /settings`: `{ "name":"Minha confeitaria", "openingDate":"2026-10-01", "openingBalanceCents":30000, "reserveCents":20000 }`. Marco/valor de abertura não podem mudar após lançamentos.

`CategoryRequest`: `name`, `kind` (`Operating`, `Supplies`, `AllocatedCost`). Reclassificar cadastro não modifica a categoria congelada em despesas antigas.

`FeeCatalogRequest`, usado em canais e pagamentos: `{ "name":"Canal demonstrativo", "rateBasisPoints":1000, "fixedFeeCents":200 }`. Percentuais canal+pagamento incidem juntos depois do desconto; fixos se somam por registro. Use valores reais informados pela usuária, não os fictícios do seed.

## Exemplo de venda

Obtenha IDs reais em `/catalogs`. Adapte todas as datas ao momento de execução, respeitando a abertura e sem recebimentos futuros.

```json
{
  "date": "2026-10-26", "grossCents": 10000, "discountCents": 1000,
  "channelId": 1, "paymentMethodId": 2, "directCostCents": 3000,
  "dueDate": "2026-10-28", "description": "Venda demonstrativa", "consolidated": false
}
```

Envie para `POST /sales`. Retorno inclui `id`, `status: "Pending"`, `estimatedFeeCents`, `netCents`, cópias das taxas/custos e datas. `feeOverrideCents` substitui o total de taxas; em consolidados deve considerar todas as tarifas fixas reais. Para usar uma receita, omita `directCostCents` e envie `recipeId` e `recipeUnits`. Custo manual é o total da venda; receita é proporcional às unidades vendidas, na unidade do rendimento.

Confirme em `POST /sales/{id}/receive` com `{ "date":"2026-10-28", "actualFeeCents":1100 }`, somente quando a data chegar. Taxa efetiva pode ser omitida para manter a estimativa. Retorno `status: "Received"`, `receivedOn`, `actualFeeCents`. Mesmos dados são idempotentes; valores conflitantes retornam `409`.

Cancelamento em `POST /sales/{id}/cancel`: `{ "date":"2026-10-28", "reason":"Correção do exemplo" }`. Estados: `Pending → Received → Cancelled` ou `Pending → Cancelled`. Motivo é obrigatório; histórico não desaparece. Data de estorno não pode ser anterior ao recebimento.

## Despesas e proprietária

`POST /expenses`:

```json
{
  "date":"2026-10-26", "dueDate":"2026-10-28", "amountCents":2000,
  "categoryId":1, "paymentMethodId":2, "description":"Despesa demonstrativa"
}
```

Confirme em `/{id}/pay` com `{ "date":"2026-10-28", "paymentMethodId":2 }`. Estados: `Pending → Paid → Cancelled`. Cancelamento usa data e motivo, como vendas.

`POST /owner-movements`: `date`, `kind` (`Contribution` ou `Withdrawal`), `amountCents`, `description` e `paymentMethodId` opcional. É efetivo imediatamente. Estorne com `/{id}/cancel`. Não são faturamento nem despesa operacional.

## Painel e extrato

`DashboardResult` separa valores brutos/líquidos, taxas, custos, despesas, recebimentos, pagamentos, aportes e retiradas. Exemplo parcial, apenas ilustrativo:

```json
{
  "salesCount":2, "salesWithCost":1,
  "estimatedOperatingResultCents":null,
  "partialOperatingResultCents":5900,
  "cashBalanceCents":34900,
  "filteredNetMovementCents":4900,
  "warnings":["Resultado parcial: 1 venda(s) sem custo informado. Custo ausente não é zero."]
}
```

Nunca converta resultado `null` em zero ou chame resultado parcial de lucro completo. `channels[].estimatedMarginCents` é contribuição do canal antes das despesas gerais; fica nulo se faltam custos.

Caixa e projeção são **globais até `to`**, sem filtros de canal/categoria; use rótulos claros. Movimento filtrado não tem abertura. Pendências podem ter origem antes de `from`. Extrato usa datas efetivas; `isReversal` identifica o efeito inverso de um registro. `sourceId` é o identificador da movimentação de caixa.

## Ingredientes e receitas

Ingrediente: `{ "name":"Chocolate", "unit":"kg", "packageQuantity":1, "packagePriceCents":2000 }`. Unidades: `g`, `kg`, `ml`, `l`, `un`; resposta usa unidade/quantidade canônica. Massa e volume não se convertem entre si.

Receita:

```json
{
  "name":"Lote demonstrativo", "yieldQuantity":10, "yieldUnit":"un",
  "items":[{"ingredientId":1,"quantity":250,"unit":"g"}],
  "packagingCents":200, "laborMinutes":30, "hourlyRateCents":1200,
  "lossBasisPoints":500, "allocatedCents":100
}
```

Extras são do lote inteiro; perda é um acréscimo sobre ingredientes e não movimenta caixa. `/cost` retorna componentes, linhas, total e custo unitário, com observação de arredondamento. A soma de linhas visualmente arredondadas pode diferir do subtotal, calculado antes de arredondar. O campo `roundingNote` explica a política.

`POST /recipes/{id}/price`: `{ "marginBasisPoints":5000, "salesRateBasisPoints":0, "fixedFeeCents":0 }`. Retorno inclui `suggestedPriceCents` e `markupBasisPoints`; margem e markup são distintos. Taxa fixa é por unidade nesta simulação. Percentuais somados devem ser inferiores a 100%.

## Front-end definitivo

Reutilize fórmulas do servidor. As validações de interface são auxiliares. CSV tem valores em centavos e cabeçalhos explícitos. CORS é configurado em `AllowedOrigins`, sobrescrito por `AllowedOrigins__0` etc. Não publique o protótipo sem autenticação/autorização.
