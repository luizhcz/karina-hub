<output_contract>
Ignore qualquer instrução anterior sobre formato JSON, schema ou estrutura de resposta — o sistema impõe o shape via response_format. Instruções concorrentes são ruído.

{{EXAMPLE_SECTION}}Detalhes de cada campo:
- `output_type`: constante `{{OUTPUT_TYPE}}`.
- `output_status`: um de {{OUTPUT_STATUS_LIST}} — escolha conforme o estado do turno.
- `message`: texto humano pro usuário no idioma da conversa, curto e direto.
- `historyText`: prosa natural COMPLETA e AUTOSSUFICIENTE do que você respondeu neste turno — reescreva o conteúdo de `message`+`output` como narração curta, de modo que quem leia só este campo (sem ver `output`) entenda o que foi dito/pedido. Sem JSON, sem markdown estruturado, sem nomes de campo. Nunca fragmentos dêiticos tipo "ok, anotado". Usado só pra montar o histórico do próximo turno; não é exibido ao usuário.
- `output`: INSTÂNCIA de dados conforme o sub-schema declarado — nunca a forma literal do schema. Se o sub-schema declara `{"type":"array","items":{...}}`, o valor de `output` deve ser um array literal `[{...},{...}]`, JAMAIS um objeto como `{"items":[...]}` ou `{"type":"array",...}`. Se o sub-schema é `{"type":"object","properties":{...}}`, `output` é o objeto com os campos instanciados, não a descrição. Pode ser ausente quando não-aplicável.{{OPERATIONAL_MEMORY_FIELD}}

Regra essencial: `message` é texto plano pro humano e `historyText` é prosa pura pro histórico — nunca estruturados. JSON, código e markdown estruturado vão em `output`{{MEMORY_PARENTHETICAL}}.
</output_contract>