# Mad Machines

«Безумные машины» — локальная альфа соревновательной физической аркады. Проект использует **Unity 6000.6.8f1** (Unity 6.6), URP и Unity Input System.

## Запуск

Используемые модели, материалы, текстуры, подготовленные колёса и их `.meta` хранятся в Git. После клонирования проекта отдельно импортировать T100 через My Assets или запускать Blender не требуется. Unity восстановит пакеты из `Packages/manifest.json` и `Packages/packages-lock.json`.

1. Открой корень репозитория в Unity 6000.6.8f1 и дождись импорта.
2. Открой [Playground.unity](Assets/MadMachines/Scenes/Playground.unity), нажми Play и выбери режим в гараже.
3. Если файла сцены нет, используй **Tools → Mad Machines → Create or open playground**.

**Сборка Windows** — **Tools → Mad Machines → Build Windows player**. Команда собирает Playground в `Builds/MadMachines/MadMachines.exe`.

## Управление

- **WASD** — газ, торможение/задний ход и поворот. **Пробел** — ручной тормоз.
- **Мышь** — обзор и наведение, **ЛКМ** — огонь. **1 / 2 / 3** — пулемёт, баллистическая пушка, тяжёлая граната.
- **C** — переключение между видом от третьего лица и прицельным видом.
- **R** — восстановление машины. **Esc** — пауза и освобождение курсора.

## Документация

- [Текущее состояние и проверка](Docs/CURRENT_STATE.md) · [Архитектура](Docs/ARCHITECTURE.md)
- [Видение игры](Docs/VISION.md) · [Этапы разработки](Docs/ROADMAP.md) · [Ближайшие задачи](Docs/BACKLOG.md)
- [Банк идей](Docs/IDEAS.md) · [Ресурсы и лицензии](Docs/ASSETS.md) · [Инструкции агентам](AGENTS.md)
