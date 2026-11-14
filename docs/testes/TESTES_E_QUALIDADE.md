# Testes realizados e acompanhamento da qualidade

## 1. Objetivo

Os testes foram utilizados para conferir se as funções da loja apresentam o comportamento esperado e se as informações são mantidas corretamente. Há registros de validação automática da API e do Angular, testes em navegador e verificações da publicação. **Esses resultados técnicos não substituem os testes solicitados a cinco colegas ou profissionais da área.**

As informações a seguir reúnem os registros anteriores e após as melhorias apontadas nos cinco relatos. O [laudo de qualidade](LAUDO_QUALIDADE.md) distingue os relatos humanos, as verificações técnicas e o reteste público ainda pendente das novas funções.

## 2. Resultados técnicos registrados

| Verificação | Resultado registrado | Alcance |
| --- | --- | --- |
| Testes da API e regras de negócio | **358 testes aprovados**, zero falhas/ignorados, em 14/11/2026. | Inclui filtros de pedidos e acesso administrativo aos autores das avaliações, com banco temporário isolado. |
| Testes do Angular | **217 testes aprovados** e build de produção aprovado em 14/11/2026. | Componentes e comportamentos da interface; não equivale a avaliação humana. |
| Fluxo de compra em navegador local | Realizado com Angular, API e PostgreSQL de teste. | Carrinho, valores, endereço, recusa, aprovação, consulta do pedido e acesso isolado por usuário. |
| Usabilidade básica | Roteiros registrados em navegador, incluindo 360 px e 1280 px. | Teclado, foco, rótulos, mensagens e adaptação de algumas telas. |
| Publicação no Render/Neon | Em 13/11/2026, o relatório registra **23 verificações HTTPS** e **6 verificações em navegador** aprovadas. | Acesso à vitrine, login administrativo, APIs, arquivos publicados e navegação principal. |

As contagens acima correspondem a conjuntos distintos de testes. Resultados intermediários do desenvolvimento não devem ser somados como se fossem novas avaliações.

## 3. Exemplos de testes de integração

| Situação testada | Resultado registrado |
| --- | --- |
| Cadastro e login | Conta de teste criada e acesso realizado pela interface integrada à API. |
| Carrinho e total | Inclusão, alteração, remoção e atualização dos valores verificadas. |
| Pagamento simulado recusado | Sem pedido aprovado e sem redução de estoque; carrinho mantido. |
| Mudança de preço antes de confirmar | Aplicação solicita revisão do novo total. |
| Pagamento simulado aprovado | Pedido criado uma única vez, estoque atualizado e carrinho esvaziado no banco de teste. |
| Consulta de pedidos | Cliente visualiza os próprios pedidos, sem acesso ao pedido de outra conta de teste. |
| Atualização administrativa | Alterações de produtos/status aparecem nas consultas respectivas, conforme relatórios por funcionalidade. |

Exemplo de evidência da compra fictícia:

![Comprovante de compra simulada](evidencias/checkout-confirmado-desktop.png)

Exemplo de acompanhamento administrativo:

![Atualização de status em teste](evidencias/status-pedido-desktop.png)

## 4. Ajustes identificados durante a revisão técnica

O [laudo de qualidade](LAUDO_QUALIDADE.md#41-origem-das-capturas-de-antes-e-depois) inclui quatro comparações visuais de antes e depois: reativação, filtros administrativos, filtros do cliente e identificação do autor das avaliações. As capturas foram obtidas de interfaces reais de commits anteriores e da versão atual, com os mesmos dados fictícios em ambiente isolado; não são imagens das sessões dos participantes.

Na revisão de navegação por teclado, foi observado que o foco nem sempre acompanhava corretamente a mudança de página. O tratamento foi ajustado e o roteiro repetido com resultado favorável. Também foi incorporado um atalho de teclado para avançar ao conteúdo principal.

Esse ajuste é uma correção técnica registrada durante o desenvolvimento, **não uma reclamação de participante**.

## 5. Limitações e verificações pendentes

- A versão acadêmica não envia e-mails, não realiza pagamentos reais e não rastreia entregadores.
- As metas de desempenho em segundos do planejamento original não foram medidas de forma conclusiva.

## 6. Testes com cinco avaliadores — relatos recebidos

O formulário da disciplina pede, para cada pessoa, **nome, data, o que funcionou, o que não funcionou e funcionalidades não testadas**. Recomenda-se fornecer o link publicado e solicitar que cada participante execute ações como consultar catálogo, cadastrar-se, montar carrinho, finalizar compra simulada e consultar pedidos.

| Participante | Data | Funcionou | Problemas e correções sugeridas | Não testado e motivo |
| --- | --- | --- | --- | --- |
| Lucas Buchner | 13/11/2026 | Funções administrativas relatadas como funcionais. | Reativação de produtos. | Reativação ausente na versão avaliada. |
| Gabryel Gimenez | 14/11/2026 | Funções administrativas relatadas como funcionais. | Filtros por data, status e ID. | Filtros ausentes na versão avaliada. |
| Tobias Gavloski | 14/11/2026 | Produtos, carrinho, pedido e avaliações. | Filtros dos pedidos por status e data. | Filtros ausentes na versão avaliada. |
| Leonardo Maros | 14/11/2026 | Produtos, carrinho, pedido e avaliações. | Filtros dos pedidos por status e data. | Filtros ausentes na versão avaliada. |
| Pedro Magalhães | 14/11/2026 | Funções administrativas relatadas como funcionais. | Ver autor da avaliação somente na administração. | Identificação ausente na versão avaliada. |

Preencher somente após os testes reais. Capturas devem registrar defeitos encontrados ou mostrar o resultado das correções, quando pertinente. As cinco opiniões deverão ser reunidas conforme a orientação acadêmica.

## 7. Estrutura sugerida para o laudo final

O laudo de qualidade não deve ser encerrado antes de avaliar os cinco registros. Ao final, incluir:

1. **Objeto avaliado:** loja virtual acadêmica e versão disponibilizada.
2. **Condições:** data, ambiente e funções testadas.
3. **Resultados:** funcionamento confirmado e problemas observados.
4. **Tratamento dos problemas:** correções aplicadas ou justificativa de não aplicação.
5. **Evidências:** imagens de telas e registros correspondentes.
6. **Conclusão:** situação da versão após as correções, com as limitações restantes.

Para a entrega, também são pedidos um vídeo narrado de demonstração com **pelo menos cinco minutos** e outro de até **cinco minutos** apresentando as alterações posteriores aos feedbacks. Os links devem ser inseridos no documento acadêmico somente após os vídeos existirem.
