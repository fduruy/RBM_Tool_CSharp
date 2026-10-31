# RBM_Tool_CSharp

Portage C# / .NET 8 de [RBM_Tool](https://github.com/fduruy/RBM_Tool) (Python) — Projet OFFSETTING.

Outil qui lit les données d'entrée (CSV), applique le matching de portefeuilles (sub-portfolios internes ↔ trades externes) et écrit les résultats en CSV et dans une base SQLite.

L'implémentation Python sert de **référence de parité** : les deux outils doivent produire les mêmes résultats sur les mêmes données.

## Structure

```text
RBM_Tool_CSharp/
├── RbmTool.sln
├── src/
│   ├── RbmTool.Core/            # bibliothèque : toute la logique
│   │   ├── Models/              # TradeInfo, Sensitivities, MatcherConfig, Match...
│   │   ├── Inputs/              # lecture/validation CSV + parsing numérique
│   │   ├── Calculations/        # identification interne/externe, orchestration
│   │   ├── Matching/            # cœur algorithmique : MatchingMath, PortfolioMatcher
│   │   ├── Outputs/             # écriture CSV
│   │   └── Database/            # SQLite (inputs + résultats)
│   └── RbmTool.Cli/             # exécutable en ligne de commande
└── tests/
    └── RbmTool.Tests/           # tests xUnit (matching, maths)
```

## Prérequis

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- VS Code (extension C# Dev Kit) ou Visual Studio 2022

## Utilisation

```bash
dotnet build
dotnet test

# Ligne de commande (depuis la racine du projet) :
# 1. Première fois : charger les CSV d'entrée dans la base SQLite
dotnet run --project src/RbmTool.Cli -- --samples --init-db   # avec data/samples/
dotnet run --project src/RbmTool.Cli -- --init-db             # avec data/input/

# 2. Run normal : les inputs sont lus depuis la base
dotnet run --project src/RbmTool.Cli

# Options :
#   --input-dir DIR    répertoire des fichiers d'entrée (défaut : data/input/)
#   --output-dir DIR   répertoire des sorties CSV (défaut : data/output/)
#   --db-path FILE     fichier SQLite (défaut : data/database/rbm_results.db)
#   --samples          utiliser data/samples/ comme source pour --init-db
#   --init-db          (re)charger les CSV dans la base avant le run
```

## Base SQLite

`data/database/rbm_results.db` contient les tables d'entrée (`gt_valo`, `rep_sensi`, `offsetting`) et de résultats (`internal_sub_portfolios`, `matched_trades`, `external_usage`, `unmatched_sensitivities`, `sub_portfolio_summary`) — toutes remplacées à chaque run.

### Lier la base à Excel en dynamique

1. Installer une fois un pilote ODBC SQLite (ex. [sqliteodbc](https://www.ch-werner.de/sqliteodbc/)).
2. Excel : **Données → Obtenir des données → À partir d'autres sources → À partir de ODBC**, pointer sur `data/database/rbm_results.db`.
3. **Actualiser tout** après chaque run (ou actualisation auto à l'ouverture du classeur).

## Parité avec le Python

Le schéma SQLite et les noms de colonnes sont identiques au projet Python : les deux outils peuvent lire/écrire la même base. Pour valider la parité :

1. Run Python : `python src/main.py --samples --init-db`
2. Run C# : `dotnet run --project src/RbmTool.Cli -- --samples --init-db`
3. Comparer les tables SQLite (ou les CSV de sortie).
