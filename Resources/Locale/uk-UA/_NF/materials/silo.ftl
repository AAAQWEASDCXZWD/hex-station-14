ore-silo-ui-nf-itemlist-entry = {$linked ->
    [true] {"[Під'єднано] "}
    *[False] {""}
} {$name} {$inRange ->
    [true] {""}
    *[false] (Поза зоною дії)
}
