# Prompt para desenvolver o MVP de gestão de docerias

Atue como desenvolvedor de back-end e implemente um protótipo funcional para a minha Avaliação Integrativa da UNIPLAC. O tema é gestão de pequenos negócios e o público são doceiras e pequenas confeitarias, especialmente quem trabalha sozinha ou por encomenda.

Minha responsabilidade no grupo é o banco de dados e o back-end. Outras pessoas farão o design e o front-end definitivo. Preciso de uma API funcional, com persistência real, regras financeiras verificáveis e poucas telas simples para demonstrar e testar os fluxos. Não concentre o trabalho no visual.

## 1 Contexto das pesquisas

As pesquisas incluem um levantamento de Meu Dinheiro, Consumer e Ouroz e os documentos:
- analise_entrevistas_qualitativas_docerias.pdf;
- Relatório de Validação de MVP - Docerias.pdf;
- Relatorio_MVP_Confeitaria.html.pdf.

A análise de Mariana e Letícia mostra que ambas possuem ou já possuíram controles financeiros. Mariana registra movimentações e consulta resultados ao final do mês; deseja organizar melhor a gestão. Letícia utiliza várias planilhas e abandonou o registro diário porque era trabalhoso e sujeito a esquecimentos e erros.

O relatório de validação acrescenta relatos atribuídos a Luana sobre lançamentos manuais, taxas de delivery e descontos pouco estruturados. Esse documento propõe registro ágil, cálculo de taxas e consolidação dos canais. Entretanto, suas afirmações de que nenhuma participante conhece o lucro são mais fortes do que a análise detalhada das entrevistas permite concluir. Não reproduza essas generalizações como fatos comprovados.

O relatório de interface propõe painel financeiro, precificação de receitas e orçamentos. Considere essas funcionalidades propostas para teste, e não necessidades universalmente validadas. Os concorrentes já apresentam vários desses recursos; não anuncie funções básicas como inéditas.

A hipótese do produto é reduzir o trabalho de registrar e somar movimentações e facilitar a consulta das informações do negócio. Priorize esse núcleo e mantenha os módulos adicionais separados. O ZIP disponibilizado contém uma página salva do Google Drive, e não os documentos das subpastas; não presuma que outros materiais foram analisados.

## 2 Decisões técnicas

- Use C# com ASP.NET Core Web API, Entity Framework Core e SQLite para a primeira versão local. Utilize uma versão LTS do .NET compatível com o ambiente e registre a versão escolhida no README e no global.json.
- Use DTOs em C# com validação de entrada, migrações do Entity Framework Core e xUnit para testes das regras críticas. Use WebApplicationFactory nos testes de integração da API quando necessário.
- Não implemente suporte simultâneo a dois bancos. Documente apenas o que precisaria ser revisto em uma futura migração para PostgreSQL.
- Separe rotas, validação de entrada, regras de negócio e persistência, sem criar camadas ou padrões desnecessários.
- Use Razor Pages com HTML e CSS básico nas telas de demonstração, no mesmo projeto ASP.NET Core quando isso simplificar a execução. A API REST deve poder ser consumida depois pelo front-end da equipe.
- Utilize injeção de dependência, configuração e logging do ASP.NET Core. Mantenha uma solução .sln, projetos .csproj e dependências NuGet com versões compatíveis. Não utilize Python ou frameworks de outra linguagem para implementar o produto.
- Não implemente microsserviços, IA, aplicativos móveis ou infraestrutura de produção.
- Se existir código no projeto, inspecione a estrutura e as instruções locais antes de alterá-lo; preserve o que for aproveitável.

O protótipo deve operar com uma única confeitaria demonstrativa. Autenticação e múltiplas empresas ficam fora desta versão. Execute em 127.0.0.1 por padrão, use somente dados fictícios e deixe claro no README que essa configuração não deve ser publicada na internet sem implementar autenticação e autorização.

## 3 Primeira etapa obrigatória do MVP

### Cadastro e configuração

Permita configurar o nome do negócio, saldo inicial de caixa com data de referência, reserva mínima de caixa e categorias de movimentação. Cadastre canais de venda, como WhatsApp, pronta entrega, iFood e Aiqfome, sem conectar contas externas. Cadastre meios de pagamento: dinheiro, Pix e cartão.

Permita cadastrar taxas percentuais e fixas por canal e meio de pagamento. Documente a base de cálculo e quando as taxas se acumulam. Não invente taxas reais dos fornecedores: os valores dos dados demonstrativos devem ser fictícios e editáveis. A reserva é uma configuração da usuária, não uma recomendação automática.

### Registro rápido de venda

Permita registrar uma venda por valor total, sem exigir itens ou cliente. Os campos essenciais são data da venda, valor bruto, canal e meio de pagamento. Desconto deve ser numérico e opcional. Permita uma descrição curta, data prevista de recebimento e custo direto informado opcionalmente.

Calcule desconto, taxas e valor líquido no servidor. Permita usar as taxas configuradas ou informar a taxa efetiva da operação. Preserve os valores utilizados como histórico da venda: alterar uma configuração não pode recalcular silenciosamente vendas antigas.

Também permita um lançamento consolidado do dia, por canal e meio de pagamento, sem obrigar o cadastro de cada doce vendido. Identifique se o registro é individual ou consolidado e informe que o consolidado não deve incluir vendas já registradas individualmente. Não afirme que é possível detectar automaticamente uma duplicidade sem identificação suficiente.

Na primeira etapa, uma venda terá um único recebimento integral: pendente ou recebido. Pagamentos parciais ficam para uma evolução posterior. A confirmação de recebimento deve atualizar o caixa uma única vez, mesmo que a requisição seja repetida. Guarde a data efetiva do recebimento e o valor recebido. Quando a taxa real divergir da estimada, registre a diferença explicitamente e atualize os totais de forma consistente.

### Despesas e retiradas

Permita registrar valor, categoria, descrição, vencimento, situação de pagamento e data efetiva do pagamento. Diferencie despesas operacionais, compras de insumos, aportes da proprietária e retiradas pessoais. Não trate aporte como faturamento ou retirada pessoal como custo do produto.

Permita despesas pendentes e pagas. Uma despesa paga reduz o caixa; uma obrigação pendente só entra na projeção. Cadastro, pagamento e cancelamento precisam manter os saldos consistentes. Registros financeiros confirmados não devem desaparecer com exclusão definitiva: ofereça cancelamento ou estorno rastreável.

### Painel e consulta

Implemente filtros por período, canal, categoria e meio de pagamento, com valores diários e mensais. Apresente separadamente:
- vendas brutas, descontos, taxas e vendas líquidas;
- recebimentos efetivos, pagamentos efetivos e saldo de caixa;
- contas a receber e contas a pagar;
- despesas operacionais, compras de insumos e retiradas pessoais;
- resultado operacional estimado, quando houver dados suficientes;
- resultados por canal, indicando quando faltarem custos para calcular a margem.

Mostre avisos simples sobre vencimentos, caixa abaixo da reserva e vendas sem custo informado. Um botão “Resumo do dia” deve consolidar automaticamente o que já foi registrado; não deve duplicar movimentações nem exigir novo lançamento dos mesmos totais. Exporte os registros e o resumo em CSV, preservando filtros e cabeçalhos compreensíveis.

## 4 Regras financeiras obrigatórias

Use valores monetários inteiros em centavos no banco e contratos documentados, representados por long em C#. Utilize decimal nos cálculos proporcionais, com uma regra explícita de arredondamento. Não use float ou double para dinheiro. Valide valores negativos, descontos superiores ao bruto e taxas incompatíveis com o valor da venda.

Mantenha a data da venda separada da data de recebimento e o vencimento separado da data de pagamento. A venda pendente pode aparecer nas vendas do período, mas não aumenta o caixa atual. Uma venda cancelada não entra no faturamento; estornos devem reverter seus efeitos sem apagar o histórico.

Adote as seguintes definições e explique-as no README e na documentação da API:

1. Vendas líquidas = vendas brutas − descontos − taxas atribuídas às vendas.
2. Caixa atual = saldo inicial + recebimentos líquidos efetivos + aportes − pagamentos efetivos − retiradas. Considere apenas movimentações posteriores ao marco do saldo inicial e os respectivos estornos.
3. Resultado operacional estimado = vendas líquidas reconhecidas no período − custos diretos associados a essas vendas − despesas operacionais do período, conforme a política simplificada documentada. Não apresente esse indicador como apuração contábil ou fiscal de lucro líquido.

Taxas já descontadas do recebimento líquido não podem ser descontadas novamente do caixa. Compras de ingredientes entram como pagamentos no caixa; se o custo consumido já compõe o custo direto das vendas, não desconte a mesma compra novamente no resultado. O custo de uma receita pode incluir rateios de mão de obra e despesas: evite contar o mesmo componente novamente nas despesas do período.

Custo desconhecido não equivale a custo zero. Se faltarem custos, informe a cobertura dos dados e mostre o resultado como parcial ou indisponível. Saldo de caixa e resultado devem aparecer com rótulos distintos. Se mostrar caixa acima da reserva, identifique-o como uma simulação limitada, sem afirmar que o valor está liberado para retirada ou que representa pró-labore.

Considere datas comerciais em America/Sao_Paulo e filtros por dia local. Preserve a integridade das operações com transações no banco, chaves estrangeiras habilitadas no SQLite, validações e restrições adequadas.

## 5 Segunda etapa obrigatória após o núcleo funcionar

Implemente cadastro básico de ingredientes, receitas e cálculo de custo. Isso deve ficar em módulo separado, sem tornar o cadastro de receitas obrigatório para registrar vendas rápidas.

- Ingrediente: nome, dimensão de medida, quantidade da embalagem e preço da embalagem.
- Receita: nome, ingredientes e quantidades utilizadas, rendimento e unidade do rendimento.
- Custos adicionais configuráveis: embalagens, tempo de trabalho com valor por hora, perda estimada e outros custos claramente classificados.
- Converta kg para g e L para mL; não converta massa em volume sem fator específico informado. Valide rendimento positivo e unidades compatíveis. Utilize decimal em C# para os cálculos de quantidades e documente uma representação precisa no SQLite. Evite depender de operações SQL com decimal que o provedor SQLite do Entity Framework Core não suporte; prefira persistência em escala inteira documentada quando necessário.
- Apresente o custo total do lote e o custo por unidade, com memória de cálculo.
- Separe ingredientes e embalagens de mão de obra e rateios para permitir relatórios sem dupla contagem.
- Diferencie markup sobre o custo de margem sobre o preço de venda.
- Para margem desejada, use preço = custo / (1 − margem − percentuais incidentes sobre a venda), incorporando taxas fixas e explicitando as hipóteses. Rejeite percentuais cujo total seja maior ou igual a 100%. Não invente impostos; use parâmetros opcionais informados pela usuária.
- Quando uma venda utilizar uma receita, preserve uma cópia dos custos e preços utilizados naquele momento. Atualizar um ingrediente muda novos cálculos, sem reescrever o histórico.
- Mantenha os custos manuais da primeira etapa como alternativa à receita, sem somar ambos ao mesmo produto vendido.

Nesta versão não implemente estoque completo, lotes, validade ou produção. Registro de perdas reais e estoque ficam para evolução; uma perda estimada na precificação não deve virar automaticamente uma despesa financeira.

## 6 Fora do escopo inicial

Não implemente integração real com bancos, marketplaces ou WhatsApp, bot por áudio, emissão de nota fiscal, cobrança automática, IA ou previsão de demanda. Os canais são classificações manuais nesta versão.

Orçamentos, pedidos com clientes, sinais, pagamentos parciais, aprovação pública, agenda de entrega e PDF personalizado são uma terceira etapa futura. Documente uma proposta breve de evolução, sem construir esses módulos antes de concluir as duas etapas obrigatórias. Não assuma que um orçamento aprovado já representa recebimento de dinheiro.

## 7 Banco de dados e API

Defina um modelo relacional enxuto para configurações do negócio, categorias, canais, meios de pagamento, regras de taxas, vendas, recebimentos, despesas, pagamentos e movimentações de aporte/retirada/estorno. Acrescente ingredientes, receitas e itens de receita na segunda etapa. Unifique entidades quando isso simplificar a implementação sem perder o significado financeiro.

Evite duas fontes independentes para o mesmo saldo. Documente a origem de cada indicador e quais eventos geram movimentação financeira. Use migrações versionadas, relacionamentos, índices justificados, datas de criação e atualização e integridade referencial.

Disponibilize uma API REST ASP.NET Core sob /api/v1 com contrato OpenAPI e uma interface interativa de documentação em /docs, configurada explicitamente para o ambiente de desenvolvimento. Inclua operações para os cadastros, vendas, recebimentos, despesas, pagamentos, retiradas, consultas, resumo diário, painel e exportação. Na segunda etapa, exponha ingredientes, receitas e cálculo de precificação. Não faça operações de alteração por GET.

Use contratos consistentes, mensagens compreensíveis, códigos HTTP apropriados, paginação e filtros. Não devolva números sem indicar sua unidade; deixe claro quando um valor está em centavos e como percentuais são representados. Documente estados e transições permitidas. Implemente idempotência na confirmação de recebimentos, pagamentos e estornos. Configure CORS por variável de ambiente para o futuro front-end.

## 8 Telas provisórias

Crie somente as telas necessárias para testar:
1. Painel com resumo e tabela de movimentações.
2. Registro rápido de venda, despesa ou retirada, com consulta e ações de confirmação/cancelamento.
3. Configurações de categorias, canais, pagamentos e taxas.
4. Ingredientes e receitas com calculadora simples, na segunda etapa.

Use formulários comuns, tabelas, mensagens de validação e CSS mínimo. Todas as operações devem passar pela mesma camada de negócio usada pela API. Os cálculos ficam no servidor e os dados ficam no SQLite, nunca apenas em localStorage ou em arrays simulados. Não desenvolva identidade visual, animações ou componentes elaborados.

## 9 Testes e critérios de aceite

Crie testes que comprovem os cálculos e as transições, principalmente:
- venda de R$ 100,00 com desconto de R$ 10,00, taxa de 10% aplicada após o desconto e taxa fixa de R$ 2,00: líquido de R$ 79,00;
- venda pendente não altera o caixa; recebê-la altera o caixa uma única vez;
- despesa pendente não reduz o caixa até o pagamento;
- retirada reduz o caixa sem reduzir o resultado operacional; aporte não aumenta o faturamento;
- taxas retidas e compra de insumos não são descontadas em duplicidade;
- cancelamentos e estornos revertem os efeitos corretos, inclusive em chamadas repetidas;
- faltas de custo não produzem lucro artificial;
- mudar taxas ou preços de ingredientes não modifica vendas anteriores;
- ingrediente de R$ 20,00 por 1 kg, usando 250 g: custo de R$ 5,00;
- custo de R$ 10,00 com margem de 50%, sem outras taxas: preço de R$ 20,00;
- conversões inválidas e percentuais de precificação impossíveis são rejeitados;
- filtros por data, canal e recebimento retornam os registros e totais esperados.

Use dados demonstrativos fictícios e uma rotina de seed repetível, sem duplicação e sem dados pessoais das entrevistadas. O arquivo SQLite deve persistir após reiniciar a aplicação.

## 10 Entregas e execução

Entregue código executável em C#, solução .sln, projetos .csproj com dependências NuGet, migrações do Entity Framework Core, seed, testes xUnit, configuração de exemplo em appsettings e README em português. Use variáveis de ambiente para sobrescrever configurações sem exigir um carregador de .env. Inclua um diagrama Mermaid do banco, exemplos de requisições e um guia curto para a equipe de front-end com endpoints, campos, estados e exemplos de respostas.

Documente no README os requisitos do SDK .NET e os comandos exatos para restaurar dependências com dotnet restore, compilar com dotnet build, aplicar migrações com dotnet ef database update, popular o banco, iniciar a aplicação com dotnet run e executar os testes com dotnet test. Inclua a instalação ou restauração da ferramenta dotnet-ef em versão compatível com o Entity Framework Core usado. Mostre também as fórmulas financeiras, hipóteses, limites e funções adiadas.

Primeiro inspecione o projeto e apresente um plano curto. Em seguida, implemente a primeira etapa, execute os testes relevantes e só então avance para receitas e precificação. Não pare na proposta nem entregue apenas telas ou pseudocódigo. Não publique nem faça deploy. Se surgir uma dúvida que não impeça o andamento, adote a opção mais simples e registre a decisão. Ao terminar, informe o que funciona, como executar, quais testes passaram e quais limitações permanecem.
