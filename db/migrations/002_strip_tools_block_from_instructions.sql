-- =============================================================================
-- 002_strip_tools_block_from_instructions.sql
--
-- Remove o bloco markdown `## Ferramentas disponíveis ...` do campo
-- `Instructions` em agent_definitions e agent_drafts. Esse bloco era
-- concatenado no system prompt do agente Custom antes da reformulação da
-- preview do ReviewStep — agora tools chegam ao LLM via function-calling
-- nativo (FunctionTool factory), e mantê-lo no texto seria redundante e
-- enganoso (PM poderia tentar editar o markdown achando que altera o
-- schema entregue ao modelo, o que não acontece).
--
-- agent_versions NÃO é tocado: snapshots de versão são imutáveis por
-- design (audit history). Versões antigas mantêm o bloco como registro
-- histórico — quando admin re-publica a partir de uma versão antiga,
-- decodeInstructions já filtra o bloco no parse (round-trip idempotente).
--
-- Idempotente: o WHERE filtra apenas rows que ainda contêm o cabeçalho,
-- então rodar várias vezes não causa efeito colateral.
--
-- Implementação usa função PL/pgSQL temporária em vez de regex puro
-- porque o engine ARE do PostgreSQL não respeita lazy quantifier `*?`
-- combinado com lookahead `(?=...|$)` — o motor opta pelo `$` mesmo em
-- modo lazy e devora até o fim da string, removendo blocos subsequentes
-- (## Output estruturado etc) por engano. Split manual é determinístico.
-- =============================================================================

CREATE OR REPLACE FUNCTION pg_temp.strip_tools_block(s text) RETURNS text AS $$
DECLARE
    header_with_nl constant text := E'\n## Ferramentas disponíveis';
    header_inline constant text := '## Ferramentas disponíveis';
    -- char_length de header_with_nl: 1 (\n) + 26 (## Ferramentas disponíveis) = 27.
    -- Usar char_length em runtime evita drift se alguém editar a constante.
    block_start int;
    body_start int;
    next_h2_rel int;
BEGIN
    IF s IS NULL OR s = '' THEN
        RETURN s;
    END IF;

    block_start := position(header_with_nl in s);
    IF block_start = 0 THEN
        -- Caso raro: bloco no início absoluto da string, sem \n prefix.
        IF position(header_inline in s) = 1 THEN
            block_start := 1;
            body_start := char_length(header_inline) + 1;
        ELSE
            RETURN s;
        END IF;
    ELSE
        body_start := block_start + char_length(header_with_nl);
    END IF;

    -- Procura próximo H2 a partir do body do bloco.
    next_h2_rel := position(E'\n## ' in substring(s from body_start));

    IF next_h2_rel = 0 THEN
        -- Sem próximo H2: corta do bloco até o fim, preservando o prefixo.
        RETURN rtrim(substring(s from 1 for block_start - 1));
    END IF;

    -- Mantém prefixo + (próximo H2 em diante), juntando com 2 quebras.
    RETURN
        rtrim(substring(s from 1 for block_start - 1))
        || E'\n\n'
        || ltrim(substring(s from body_start + next_h2_rel - 1));
END;
$$ LANGUAGE plpgsql IMMUTABLE;

-- agent_definitions (estado vivo dos agentes publicados)
UPDATE aihub.agent_definitions
SET "Data" = jsonb_set(
        "Data"::jsonb,
        '{Instructions}',
        to_jsonb(pg_temp.strip_tools_block("Data"::jsonb ->> 'Instructions'))
    )::text,
    "UpdatedAt" = "UpdatedAt"  -- preserva timestamp; limpeza não é edição lógica
WHERE "Data"::jsonb ->> 'Instructions' LIKE '%## Ferramentas disponíveis%';

-- agent_drafts (rascunhos em fluxo)
UPDATE aihub.agent_drafts
SET "Data" = jsonb_set(
        "Data"::jsonb,
        '{Instructions}',
        to_jsonb(pg_temp.strip_tools_block("Data"::jsonb ->> 'Instructions'))
    )::text,
    "UpdatedAt" = "UpdatedAt"
WHERE "Data"::jsonb ->> 'Instructions' LIKE '%## Ferramentas disponíveis%';

-- pg_temp é descartada ao fim da sessão; sem cleanup explícito.
