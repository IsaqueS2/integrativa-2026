# Confeitaria — protótipo de gestão financeira

Avaliação Integrativa UNIPLAC. API em C# com ASP.NET Core, Entity Framework Core e SQLite, com quatro telas Razor simples. O front-end definitivo pode consumir a mesma API. O produto testa a hipótese de simplificar registros e consultas; as pesquisas não demonstram que todas as entrevistadas desconhecem o lucro.

## Funcionalidades implementadas

- Registro de vendas individuais ou consolidadas, descontos e taxas por canal e pagamento.
- Contas a receber e a pagar, confirmação de recebimentos/pagamentos e estornos com histórico.
- Aportes e retiradas pessoais separados das receitas e despesas do negócio.
- Painel de caixa e resultado operacional estimado, filtros, resumo diário e exportações CSV e relatório colorido para PDF.
- Cadastro de ingredientes e receitas, custo proporcional, precificação por margem e controle de estoque consumido por vendas vinculadas a receitas.
- Importação revisável de compras a partir de XML de NF-e, PDF ou imagem, com criação de despesa e atualização de ingredientes, custos e estoque após confirmação.
- API REST documentada em OpenAPI e telas provisórias para testar os fluxos.

## Tecnologias

| Parte | Tecnologia |
|---|---|
| Linguagem e plataforma | C# e .NET 10 LTS |
| Back-end | ASP.NET Core Web API |
| Banco de dados | SQLite local/testes e PostgreSQL (Supabase) em produção, com Entity Framework Core |
| Telas de demonstração | Razor Pages, HTML, CSS e JavaScript |
| Testes | xUnit e WebApplicationFactory com SQLite real |
| Documentação da API | OpenAPI e Swagger UI |

## Executar

Requisito: SDK **.NET 10 LTS**. Desenvolvimento com SDK **10.0.103**, runtime **10.0.3**. `global.json` admite uma versão posterior da família 10.0; EF Core e a ferramenta de migração usam 10.0.3.

Para obter o projeto:

```powershell
git clone https://github.com/IsaqueS2/integrativa-2026.git
cd integrativa-2026
```

No terminal da raiz do repositório:

```powershell
dotnet restore
dotnet tool restore
dotnet build
dotnet ef database update --project src/Confeitaria.Web
dotnet run --project src/Confeitaria.Web --launch-profile local -- --seed-demo
```

- Telas: **http://127.0.0.1:5080/**
- API interativa: **http://127.0.0.1:5080/docs**, somente em Development.
- OpenAPI: `/swagger/v1/swagger.json`. Saúde: `/health`.

## Publicar no Render

O repositório inclui um `Dockerfile` e um Blueprint em `render.yaml` para publicar a aplicação como serviço Web gratuito:

1. Envie o repositório para o GitHub.
2. No Render, escolha **New → Blueprint** e conecte o repositório.
3. Confirme a criação do serviço `confeitaria`. O Render compila a imagem, aplica as migrações na inicialização e fornece um endereço `*.onrender.com`.

O Blueprint configura PostgreSQL e deixa `ConnectionStrings__Database` para preenchimento seguro no painel do Render. Crie ou selecione o projeto Supabase e copie os dados em **Connect**. Configure a string no formato Npgsql (`Host=...;Port=...;Database=postgres;Username=...;Password=...;SSL Mode=Require;Maximum Pool Size=10`), sem incluí-la no repositório. Use a conexão direta se o serviço Render alcançar o endpoint; em uma rede somente IPv4, use o **Session pooler** do Supabase. Não use o **Transaction pooler** para esta API. As migrações EF Core são aplicadas pela aplicação na inicialização; a API valida o contexto/provedor e interrompe o startup se encontrar histórico de migrações SQLite no PostgreSQL. `SeedDemo=false` mantém a API sem lançamentos fictícios.

Depois de salvar a variável no painel do Render, o deploy pode iniciar a API e aplicar as migrações. O plano gratuito do Render pode suspender o serviço, mas os dados permanecem no PostgreSQL do Supabase. Não há migração automática dos dados de um arquivo SQLite local para o Supabase; exporte/import dados separadamente se precisar preservá-los.

O `AllowedHosts` aceita `*.onrender.com` para que o filtro de Host do ASP.NET Core permita a URL padrão do serviço Render mesmo se o serviço tiver sido criado fora deste Blueprint. Para domínio personalizado, configure explicitamente esse hostname em `AllowedHosts` no Render.

Se uma inicialização falhar durante uma migração, pause novos deploys e não presuma que um novo retry é seguro. Antes de reimplantar, faça backup e confira o histórico e as tabelas no Supabase conforme [o procedimento de recuperação do banco](docs/DATABASE.md#falha-de-migração-em-produção). A correção impede novas migrações automáticas quando detecta IDs SQLite aplicados, mas não remove nem reconcilia objetos que já existam.

**Atenção:** a aplicação não tem autenticação nem separação de usuários. Qualquer pessoa com o endereço pode consultar e alterar os dados. Não use dados financeiros reais nem exponha informações pessoais. Um deploy público para uso real exige autenticação e autorização antes de ser considerado seguro.

Para um front-end hospedado em outro domínio, adicione a origem exata em `AllowedOrigins__0` nas variáveis de ambiente do serviço Render.

| Tela | Caminho |
|---|---|
| Painel financeiro | `/` |
| Vendas, despesas, aportes e retiradas | `/Movements` |
| Negócio, categorias, canais e taxas | `/Settings` |
| Ingredientes, receitas e preços | `/Recipes` |

Em desenvolvimento, o banco SQLite local fica em `src/Confeitaria.Web/confeitaria.db`. As migrações também são aplicadas no início da aplicação. Encerre com `Ctrl+C`.

Sem `--seed-demo`, somente configurações e cadastros básicos são criados. Com o argumento, o seed cria lançamentos fictícios apenas se o banco ainda não tiver lançamentos, além de uma receita demonstrativa que não é duplicada. O seed não limpa bancos existentes. **Taxas demonstrativas são fictícias e editáveis; não são preços dos marketplaces.**

Para um banco SQLite demonstrativo separado, sem apagar o atual:

```powershell
$env:ConnectionStrings__Database = 'Data Source=outra-demonstracao.db;Foreign Keys=True;Default Timeout=30'
dotnet run --project src/Confeitaria.Web --launch-profile local -- --seed-demo
# Depois de encerrar, remova a variável deste terminal para voltar à configuração original:
Remove-Item Env:ConnectionStrings__Database
```

## Testes

```powershell
dotnet test
```

Os testes usam SQLite real, migrações reais e bancos temporários independentes. Cobrem cálculo de taxas, concorrência/idempotência, estornos, caixa histórico, filtros, custos ausentes, precificação e preservação de custos antigos. Não alteram o banco demonstrativo.

Validação atual: **42 testes aprovados**, incluindo importação de compras, atualização/baixa/restauração de estoque e geração do documento OpenAPI.

## Organização

```text
src/Confeitaria.Web/
  Api/          Rotas HTTP
  Contracts/    DTOs
  Domain/       Entidades
  Data/         DbContext, migrações e seed
  Services/     Regras financeiras e receitas
  Pages/        Telas Razor provisórias
  wwwroot/      CSS mínimo e JS que consome a API
tests/Confeitaria.Tests/
docs/           Modelo relacional e guia para o front-end
```

## Fluxos

1. Configure a abertura e a reserva; cadastre canais, pagamentos, categorias e taxas.
2. Registre uma venda individual ou consolidada do dia. Não inclua no consolidado vendas individuais já registradas: sem identificador externo suficiente, essa sobreposição não pode ser detectada automaticamente.
3. Confirme o recebimento integral quando o dinheiro entrar. Informe a taxa efetiva se diferir da estimativa.
4. Cadastre despesas e confirme pagamentos separadamente.
5. Registre aportes e retiradas pessoais como movimentos próprios.
6. Consulte painel, resumo do dia e exporte CSV ou relatório colorido para imprimir/salvar como PDF. Resumo é somente leitura: não cria outro lançamento.
7. Cadastre ingredientes e receitas e simule preços. Receitas são opcionais para vendas rápidas. Compras importadas ficam como despesas `Supplies`; ao vender com receita, o estoque controlado é baixado e, ao cancelar a venda, a quantidade é restaurada.
8. Em `/Recipes`, anexe XML de NF-e, PDF ou imagem para extrair uma prévia. Revise e edite os itens, unidades e ingredientes antes de confirmar. O sistema não envia o documento a serviços externos nem guarda o arquivo original; registra hash/chave fiscal para evitar importações repetidas.

### Importação de compras e estoque

São aceitos XML, PDF e imagens PNG, JPG/JPEG, TIFF, BMP ou WebP, limitados a 10 MB por arquivo. PDFs têm limite de 20 páginas. XML de NF-e e PDFs com texto são extraídos diretamente; PDFs digitalizados e imagens usam OCR local com o modelo português incluído em `src/Confeitaria.Web/tessdata/por.traineddata`. A leitura pode conter erros, por isso a prévia é editável e nada é gravado até a confirmação.

A confirmação cria uma despesa na categoria `Supplies`, atualiza o custo do pacote e soma a quantidade ao estoque em uma transação. Ingredientes antigos sem controle de estoque permanecem com saldo nulo até serem vinculados a uma compra. A importação não substitui conferência fiscal ou contábil; valide valores, unidades e itens reconhecidos antes de confirmar.

## Dinheiro e taxas

Dinheiro: `long` em **centavos**. Proporções: `decimal`. Pontos-base: `10000 = 100%`, `1000 = 10%`. Arredondamento financeiro: meio centavo para longe de zero.

```text
base = bruto − desconto
taxa = arredondar(base × (percentual canal + percentual pagamento) / 10000)
       + taxa fixa canal + taxa fixa pagamento
líquido = bruto − desconto − taxa
```

Percentuais se somam sobre a mesma base, após desconto. Taxas fixas se somam e se aplicam por registro. Em um consolidado de várias operações com taxas fixas, informe o total efetivo em `feeOverrideCents`: o sistema não sabe quantas cobranças ocorreram. A taxa efetiva de recebimento substitui a estimativa; nunca é descontada duas vezes. Alterar cadastros não reescreve os valores congelados nas vendas.

## Caixa e resultado

```text
caixa = abertura + recebimentos líquidos + aportes − pagamentos − retiradas
resultado operacional estimado = vendas líquidas − custo das vendas − despesas Operating
```

O caixa usa datas efetivas de recebimentos/pagamentos. O resultado usa as datas de origem das vendas e despesas, inclusive pendentes, em uma política simplificada de competência. Não é apuração contábil/fiscal de lucro líquido. Abertura representa o início de `openingDate`; movimentos na mesma data são posteriores à abertura. Eventos anteriores ao marco ou no futuro são rejeitados. A abertura não pode mudar depois do primeiro lançamento.

O custo manual é o total da venda. Ao usar receita, o custo é proporcional às unidades vendidas e a memória de cálculo é congelada. Receita e custo manual são alternativas. Uma compra não pode ser descontada novamente se o consumo já compõe o custo vendido.

| Categoria | Caixa ao pagar | Resultado |
|---|---|---|
| `Operating` | Reduz | Deduz despesa operacional |
| `Supplies` | Reduz | Não deduz de novo: consumo está no custo vendido |
| `AllocatedCost` | Reduz | Não deduz de novo: já rateado no custo da receita |

Se apenas parte de uma despesa está rateada nas receitas vendidas, divida o lançamento entre a parte `AllocatedCost` e a parte `Operating`. Esse rateio não é automático. Nem todos os custos/despesas ausentes podem ser detectados pelo sistema. Uma venda sem custo informado torna `estimatedOperatingResultCents` nulo; o painel mostra cobertura e resultado parcial com ressalva. Custo desconhecido não é zero.

## Datas, histórico e filtros

- Datas comerciais são `DateOnly` em `America/Sao_Paulo`; auditoria de criação/atualização usa UTC.
- Filtros `from`/`to` são inclusivos; padrão é o mês corrente até hoje.
- Venda/despesa usa data de origem; extrato usa data efetiva e data de estorno.
- Confirmação repetida com mesma data/valores não duplica caixa. Confirmação conflitante retorna `409`.
- Cancelamentos não apagam eventos. Um pagamento/recebimento estornado produz efeito inverso na data do estorno e preserva o caixa histórico anterior.
- Vendas/despesas canceladas até a data de corte são excluídas do resultado. Antes do recebimento, o histórico usa taxa estimada; depois, usa a efetiva.
- Categoria filtra despesas/pagamentos, canal filtra vendas/recebimentos. Aportes/retiradas não pertencem a categoria ou canal.
- `cashBalanceCents`, `projectedCashCents` e os avisos globais representam o negócio **inteiro** até `to`, sem filtro de categoria/canal. Os rótulos deixam isso explícito.
- `filteredNetMovementCents` soma somente eventos do período/filtros, sem abertura.
- Contas a receber/pagar incluem pendências conhecidas na data de corte, inclusive com origem anterior a `from`, respeitando filtros aplicáveis.
- Projeção geral = caixa global + todos os recebíveis pendentes − todos os pagamentos pendentes conhecidos, sem previsão de probabilidade ou data única de realização.
- Reserva é definida pela usuária. O sistema não afirma qual valor pode ser retirado nem recomenda pró-labore.

## Receitas e precificação

Quantidades são armazenadas como inteiros em escala de 1.000.000 por g, mL ou unidade. Conversões kg/g e L/mL são exatas; massa e volume não são intercambiáveis. Rendimento e tempo também usam escala inteira. Preços dos ingredientes alteram novas consultas, preservando vendas antigas.

Ingredientes são somados antes do arredondamento do subtotal; perda sobre ingredientes e mão de obra são arredondadas separadamente. Extras monetários são valores do lote. As linhas visuais arredondadas podem diferir do subtotal em centavos; a resposta informa isso. O custo unitário exibido é arredondado; preço e custo de venda usam a proporção exata do custo total do lote.

```text
preço por unidade = (custo proporcional por unidade + taxa fixa por unidade)
                   / (1 − margem desejada − percentuais sobre a venda)
markup = (preço / custo − 1) × 100%
```

O preço sugerido arredonda para cima ao centavo para não reduzir a margem desejada. Margem incide no preço, markup no custo. Soma de percentuais maior ou igual a 100% é rejeitada. Taxas da simulação são parâmetros informados, não impostos oficiais. Perda estimada é acréscimo no custo, sem gerar despesa financeira ou movimentação de estoque.

## Integração e configuração

Consulte [o guia do front-end](docs/FRONTEND.md) e [o diagrama do banco](docs/DATABASE.md). DTOs camelCase, enums como strings em inglês e erros Problem Details com `detail` em português. As telas não calculam regras financeiras nem armazenam dados financeiros no navegador; somente a preferência de layout do painel é lembrada localmente.

`appsettings.json` configura SQLite local sem segredos. `Database:Provider` aceita `Sqlite` ou `Postgres`; variáveis de ambiente usam `__`, por exemplo `Database__Provider=Postgres`, `ConnectionStrings__Database`, `AllowedOrigins__0` e `SeedDemo`. Não é necessário um carregador `.env`. CORS permite apenas origens configuradas; por padrão, o futuro front-end local na porta 5173.

## Limitações e evolução

Uma confeitaria, sem autenticação ou múltiplos usuários. O deploy Render incluído é público e voltado somente a demonstrações com dados fictícios. **Não publique dados reais:** autenticação, autorização, isolamento por empresa e proteção dos dados precisam ser implementados antes de uso real.

Um recebimento integral por venda e um pagamento integral por despesa. Sem estoque físico, lotes, validade, produção, integração externa, nota fiscal, cobrança automática ou IA. Lançamentos financeiros são corrigidos por cancelamento/estorno e novo registro, não por exclusão definitiva. Listas de ingredientes/receitas têm paginação; as telas provisórias usam até os primeiros 100 cadastros.

Relatórios calculam eventos de um único negócio em memória, adequados ao protótipo. Para grandes volumes, mover agregações e paginação para SQL e medir índices. Limite de cada valor monetário de entrada: 1.000.000.000.000 centavos.

Evoluções futuras: clientes, orçamentos, pedidos, sinais e pagamentos parciais, agenda e PDF. Orçamento aprovado não representa dinheiro recebido. SQLite e PostgreSQL têm conjuntos de migrações EF separados; o contexto Postgres aplica as migrações próprias sem alterar as do SQLite. Não há transferência automática de dados entre bancos.
