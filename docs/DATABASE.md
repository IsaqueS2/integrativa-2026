# Modelo relacional

Um negócio local. O saldo é derivado da abertura e das movimentações efetivas/estornos, sem coluna independente de saldo para ficar dessincronizada.

```mermaid
erDiagram
    Channels ||--o{ Sales : origem
    PaymentMethods ||--o{ Sales : meio
    Categories ||--o{ Expenses : classifica
    PaymentMethods ||--o{ Expenses : meio
    Sales ||--o| CashEntries : recebe
    Expenses ||--o| CashEntries : paga
    PaymentMethods ||--o{ CashEntries : meio
    Recipes ||--o{ RecipeItems : contem
    Ingredients ||--o{ RecipeItems : utiliza
    Recipes ||--o{ Sales : custo_snapshot
    Settings {
        int Id PK
        string Name
        date OpeningDate
        long OpeningBalanceCents
        long ReserveCents
    }
    Sales {
        int Id PK
        date Date
        date DueDate
        int ChannelId FK
        int PaymentMethodId FK
        long GrossCents
        long DiscountCents
        long EstimatedFeeCents
        long ActualFeeCents
        long DirectCostCents
        int RecipeId FK
        string CostSnapshotJson
        date ReceivedOn
        date CancelledOn
    }
    Expenses {
        int Id PK
        int CategoryId FK
        string KindSnapshot
        date Date
        date DueDate
        long AmountCents
        date PaidOn
        date CancelledOn
    }
    CashEntries {
        int Id PK
        date Date
        string Kind
        long AmountCents
        int SaleId FK
        int ExpenseId FK
        date ReversedOn
        string ReversalReason
    }
    Ingredients {
        int Id PK
        string Name
        string Dimension
        long PackageQuantityMicros
        long PackagePriceCents
    }
    Recipes {
        int Id PK
        string Name
        long YieldQuantityMicros
        string YieldUnit
        long PackagingCents
        long LaborMinutesMicros
        long HourlyRateCents
        int LossBasisPoints
        long AllocatedCents
    }
    RecipeItems {
        int Id PK
        int RecipeId FK
        int IngredientId FK
        long QuantityMicros
    }
```

Custos, taxas efetivas, vínculos de receita e datas de confirmação/cancelamento são nulos quando não conhecidos/aplicáveis. `CashEntries` também registra aportes/retiradas, sem origem em venda/despesa. O valor é absoluto; sinal deriva de `Kind` e se inverte em `ReversedOn`.

Entidades possuem criação/atualização UTC. Taxas e classificações têm cópias nas operações; receita usada na venda gera JSON imutável da memória de custo. Mudanças de ingredientes não recalculam vendas passadas.

Restrições: chaves estrangeiras habilitadas, exclusões restritas, valores positivos/não negativos, descontos/taxas limitados à venda e nomes únicos nos cadastros. Índices de datas/canal/pagamento/categoria apoiam consultas. Índices únicos de movimentação por venda/despesa previnem duplicação. Confirmações usam transações com bloqueio de escrita SQLite; atualizações de receita e itens também são atômicas.

Migrações em `src/Confeitaria.Web/Data/Migrations`: `InitialFinance` e `RecipesAndPricing`. Use `dotnet ef migrations add Nome --project src/Confeitaria.Web` e `dotnet ef database update --project src/Confeitaria.Web`. Não use `EnsureCreated` no aplicativo.
