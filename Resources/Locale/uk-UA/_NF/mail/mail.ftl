ent-BaseMailLarge = пакунок
mail-large-item-name-addressed = пакунок ({$recipient})
mail-large-desc-far = Великий пакунок.
mail-large-desc-close = Великий пакунок для {CAPITALIZE($name)}, {$job}. Останнє відоме місце перебування: {$station}.

command-mailto-no-mailservice = Не вдалося отримати доступ до поштової служби.

# Команди поштової доставки
command-mailto-description = Додати предмет до черги доставки одержувачу. Наприклад: `mailto 1234 5678 false false`. Якщо цільова сутність є контейнером, його вміст перекладуть у поштову посилку.
command-mailto-help = Використання: {$command} <recipient entityUid> <contents entityUid> [is-fragile: true|false] [is-priority: true|false] [is-large: true|false]
command-mailto-no-mailreceiver = У цільової сутності-одержувача немає компонента {$requiredComponent}.
command-mailto-no-blankmail = Прототипу {$blankMail} не існує. Сталася серйозна помилка — зверніться до програміста.
command-mailto-bogus-mail = У {$blankMail} немає компонента {$requiredMailComponent}. Сталася серйозна помилка — зверніться до програміста.
command-mailto-invalid-container = Цільова сутність-контейнер не має контейнера {$requiredContainer}.
command-mailto-unable-to-receive = Не вдалося налаштувати цільову сутність для отримання пошти. Можливо, у неї немає ID-картки.
command-mailto-no-teleporter-found = Не вдалося знайти поштовий телепортер станції для цільової сутності. Можливо, одержувач перебуває поза станцією.
command-mailto-success = Готово! Посилку додано до черги на телепортацію через {$timeToTeleport} с.

# Підказки аргументів команди mailto
command-mailto-completion-recipient = <recipient entityUid>
command-mailto-completion-container = <contents entityUid>
command-mailto-completion-fragile = [is-fragile: true|false]
command-mailto-completion-priority = [is-priority: true|false]
command-mailto-completion-large = [is-large: true|false]

command-mailnow = Наказати всім поштовим телепортерам якнайшвидше доставити наступну партію пошти. Ліміт недоставленої пошти не змінюється.
command-mailnow-help = Використання: {$command}
command-mailnow-success = Готово! Незабаром усі поштові телепортери доставлять наступну партію пошти.
