-- =============================================================================
-- EfsAiHub — Model Catalog Seed
-- PostgreSQL 16+ · Schema: aihub
--
-- Executar APÓS schema.sql.
-- Uso:
--   psql -U <usuario> -d <banco> -f model_catalog_seed.sql
--
-- Idempotente: usa INSERT ... ON CONFLICT DO UPDATE para atualizar
-- display_name/description/context_window/capabilities se já existirem.
-- =============================================================================

SET search_path TO aihub;

-- =============================================================================
-- OpenAI
-- =============================================================================

INSERT INTO aihub.model_catalog (id, provider, display_name, description, context_window, capabilities, is_active, created_at, updated_at)
VALUES
    ('gpt-4o',              'OPENAI', 'GPT-4o',            'Multimodal flagship — texto, visão e function calling',    128000, '["chat","vision","function_calling"]',       true, NOW(), NOW()),
    ('gpt-4o-mini',         'OPENAI', 'GPT-4o Mini',       'Versão compacta e econômica do GPT-4o',                    128000, '["chat","vision","function_calling"]',       true, NOW(), NOW()),
    ('gpt-4.1',             'OPENAI', 'GPT-4.1',           'GPT-4 de nova geração com melhorias de instrução',        1047576, '["chat","function_calling"]',                true, NOW(), NOW()),
    ('gpt-4.1-mini',        'OPENAI', 'GPT-4.1 Mini',      'Versão econômica do GPT-4.1',                             1047576, '["chat","function_calling"]',                true, NOW(), NOW()),
    ('gpt-4.1-nano',        'OPENAI', 'GPT-4.1 Nano',      'Modelo ultra-leve para tarefas simples de alta velocidade',1047576, '["chat","function_calling"]',               true, NOW(), NOW()),
    ('o3',                  'OPENAI', 'o3',                 'Modelo de raciocínio avançado',                            200000, '["chat","reasoning"]',                       true, NOW(), NOW()),
    ('o4-mini',             'OPENAI', 'o4-mini',            'Modelo de raciocínio compacto e eficiente',               200000, '["chat","reasoning","function_calling"]',    true, NOW(), NOW()),
    -- Família GPT-5.x — preços em db/seed_model_pricing.sql (2026)
    ('gpt-5.2',             'OPENAI', 'GPT-5.2',           'GPT-5 geração anterior ainda disponível como fallback',    272000, '["chat","vision","function_calling"]',       true, NOW(), NOW()),
    ('gpt-5.3',             'OPENAI', 'GPT-5.3',           'GPT-5 intermediário com raciocínio e tool use',            272000, '["chat","vision","function_calling","reasoning"]', true, NOW(), NOW()),
    ('gpt-5.4',             'OPENAI', 'GPT-5.4',           'GPT-5 flagship atual — raciocínio + multimodal + long context', 272000, '["chat","vision","function_calling","reasoning"]', true, NOW(), NOW()),
    ('gpt-5.4-mini',        'OPENAI', 'GPT-5.4 Mini',      'GPT-5.4 com custo reduzido (~30% do flagship)',            272000, '["chat","vision","function_calling"]',       true, NOW(), NOW()),
    ('gpt-5.4-nano',        'OPENAI', 'GPT-5.4 Nano',      'Modelo ultra-leve da família 5.4 para alta velocidade',    272000, '["chat","function_calling"]',                true, NOW(), NOW())
ON CONFLICT (id, provider) DO UPDATE SET
    display_name   = EXCLUDED.display_name,
    description    = EXCLUDED.description,
    context_window = EXCLUDED.context_window,
    capabilities   = EXCLUDED.capabilities,
    updated_at     = NOW();

-- =============================================================================
-- Azure OpenAI
-- =============================================================================

INSERT INTO aihub.model_catalog (id, provider, display_name, description, context_window, capabilities, is_active, created_at, updated_at)
VALUES
    ('gpt-4o',         'AZUREOPENAI', 'GPT-4o (Azure)',       'GPT-4o via Azure OpenAI Service',       128000, '["chat","vision","function_calling"]', true, NOW(), NOW()),
    ('gpt-4o-mini',    'AZUREOPENAI', 'GPT-4o Mini (Azure)',  'GPT-4o Mini via Azure OpenAI Service',  128000, '["chat","vision","function_calling"]', true, NOW(), NOW()),
    ('gpt-4.1',        'AZUREOPENAI', 'GPT-4.1 (Azure)',      'GPT-4.1 via Azure OpenAI Service',     1047576, '["chat","function_calling"]',          true, NOW(), NOW()),
    ('gpt-4.1-mini',   'AZUREOPENAI', 'GPT-4.1 Mini (Azure)', 'GPT-4.1 Mini via Azure OpenAI Service',1047576, '["chat","function_calling"]',          true, NOW(), NOW()),
    -- Família GPT-5.x via Azure OpenAI (paridade pay-as-you-go com OpenAI direct — ver seed_model_pricing.sql)
    ('gpt-5.2',        'AZUREOPENAI', 'GPT-5.2 (Azure)',      'GPT-5.2 via Azure OpenAI Service',      272000, '["chat","vision","function_calling"]', true, NOW(), NOW()),
    ('gpt-5.3',        'AZUREOPENAI', 'GPT-5.3 (Azure)',      'GPT-5.3 via Azure OpenAI Service',      272000, '["chat","vision","function_calling","reasoning"]', true, NOW(), NOW()),
    ('gpt-5.4',        'AZUREOPENAI', 'GPT-5.4 (Azure)',      'GPT-5.4 flagship via Azure OpenAI Service', 272000, '["chat","vision","function_calling","reasoning"]', true, NOW(), NOW()),
    ('gpt-5.4-mini',   'AZUREOPENAI', 'GPT-5.4 Mini (Azure)', 'GPT-5.4 Mini via Azure OpenAI Service', 272000, '["chat","vision","function_calling"]', true, NOW(), NOW()),
    ('gpt-5.4-nano',   'AZUREOPENAI', 'GPT-5.4 Nano (Azure)', 'GPT-5.4 Nano via Azure OpenAI Service', 272000, '["chat","function_calling"]',          true, NOW(), NOW())
ON CONFLICT (id, provider) DO UPDATE SET
    display_name   = EXCLUDED.display_name,
    description    = EXCLUDED.description,
    context_window = EXCLUDED.context_window,
    capabilities   = EXCLUDED.capabilities,
    updated_at     = NOW();

-- =============================================================================
-- Azure AI Foundry
-- =============================================================================

INSERT INTO aihub.model_catalog (id, provider, display_name, description, context_window, capabilities, is_active, created_at, updated_at)
VALUES
    ('gpt-4o',                  'AZUREFOUNDRY', 'GPT-4o (Foundry)',              'GPT-4o via Azure AI Foundry',             128000, '["chat","vision","function_calling"]', true, NOW(), NOW()),
    ('Meta-Llama-3.3-70B-Instruct', 'AZUREFOUNDRY', 'Llama 3.3 70B Instruct',   'Meta Llama 3.3 70B via Azure AI Foundry', 128000, '["chat","function_calling"]',          true, NOW(), NOW()),
    ('Phi-4',                   'AZUREFOUNDRY', 'Phi-4',                         'Microsoft Phi-4 via Azure AI Foundry',    16000,  '["chat","function_calling"]',          true, NOW(), NOW()),
    ('Phi-4-mini-instruct',     'AZUREFOUNDRY', 'Phi-4 Mini Instruct',           'Phi-4 Mini via Azure AI Foundry',         128000, '["chat","function_calling"]',          true, NOW(), NOW()),
    ('DeepSeek-R1',             'AZUREFOUNDRY', 'DeepSeek R1',                   'DeepSeek R1 raciocínio via Azure AI Foundry', 64000, '["chat","reasoning"]',             true, NOW(), NOW())
ON CONFLICT (id, provider) DO UPDATE SET
    display_name   = EXCLUDED.display_name,
    description    = EXCLUDED.description,
    context_window = EXCLUDED.context_window,
    capabilities   = EXCLUDED.capabilities,
    updated_at     = NOW();

-- =============================================================================
-- FIM DO SEED
-- =============================================================================
