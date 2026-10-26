ore-silo-ui-title = Сховище матеріалів
ore-silo-ui-label-clients = Машини
ore-silo-ui-label-mats = Матеріали
ore-silo-ui-itemlist-entry = {$linked ->
    [true] {"[Під'єднано] "}
    *[False] {""}
} {$name} ({$beacon}) {$inRange ->
    [true] {""}
    *[false] (Поза радіусом дії)
}
