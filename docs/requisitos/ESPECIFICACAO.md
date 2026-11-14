# Requisitos e escopo da solução

## 1. Contexto e objetivo

A loja de cupcakes gourmet foi proposta na PIT I como uma forma de ampliar as vendas por meio digital. Naquela etapa, foram organizadas **15 histórias de usuário** com prioridades e critérios de aceitação. O trabalho da PIT II deu continuidade ao planejamento, desenvolvendo uma aplicação web responsiva para demonstrar o fluxo de compra e a administração da loja.

O objetivo é disponibilizar um sistema simples e compreensível, capaz de apresentar produtos, registrar pedidos fictícios e permitir o acompanhamento do atendimento. A aplicação é uma demonstração acadêmica, não um comércio eletrônico em operação.

## 2. Perfis de usuários

| Perfil | Principais ações |
| --- | --- |
| Visitante | Consultar produtos, utilizar filtros e criar uma conta. |
| Cliente | Entrar na conta, organizar carrinho, confirmar pedido simulado, consultar pedidos e avaliar produtos. |
| Administrador | Manter produtos, estoque e status dos pedidos; consultar relatórios de vendas simuladas. |

O cadastro público cria contas de cliente. As funções administrativas exigem permissão específica.

## 3. Histórias de usuário aproveitadas da PIT I

As prioridades A, B e C representam a organização original do backlog, não o estado atual do sistema.

| História | Prioridade | Funcionalidade contemplada na versão |
| --- | --- | --- |
| US01 | A | Cadastro com e-mail e senha; confirmação apresentada na tela. |
| US02 | A | Login com validação de credenciais e bloqueio temporário após tentativas incorretas. |
| US03 | A | Logout para encerrar a sessão. |
| US04 | A | Vitrine com descrição, preço, imagem demonstrativa e disponibilidade dos produtos. |
| US05 | B | Filtros por sabor, faixa de preço e promoção, acessíveis também a visitantes. |
| US06 | A | Inclusão de produtos e quantidades no carrinho. |
| US07 | B | Alteração de quantidade e remoção de itens do carrinho. |
| US08 | A | Cálculo de subtotal, descontos de produtos e valor total. |
| US09 | A | Escolha de PIX ou cartão **simulados**, com cenários de aprovação e recusa. |
| US10 | A | Confirmação na tela, número de referência e consulta posterior do pedido. |
| US11 | B | Acompanhamento do status do pedido atualizado manualmente pelo administrador. |
| US12 | B | Cadastro de produtos pela administração. |
| US13 | B | Edição de produtos, atualização de estoque e desativação do catálogo público. |
| US14 | C | Relatórios de vendas simuladas por dia ou semana, com opções de PDF e planilha. |
| US15 | C | Avaliações com nota e comentário, publicadas e editadas pelo próprio cliente. |

A gestão administrativa de pedidos foi incluída como apoio à US11, para permitir que os pedidos avancem pelos estados previstos.

## 4. Regras de funcionamento

- **Acesso:** o usuário se identifica por e-mail e senha. O sistema restringe as operações administrativas e impede que um cliente consulte os pedidos de outra pessoa.
- **Produtos:** somente produtos ativos aparecem na vitrine. Preços promocionais, quando existentes, precisam ser inferiores ao preço normal. O estoque não pode ser negativo.
- **Carrinho:** o cliente pode adicionar, alterar e remover itens. O estoque é conferido novamente na finalização, porque adicionar ao carrinho não reserva unidades.
- **Pedido:** o servidor calcula o total usando os preços atuais. Após aprovação simulada, o pedido preserva os nomes e os preços utilizados naquela compra, mesmo que o catálogo seja modificado depois.
- **Pagamento:** a opção PIX ou cartão serve exclusivamente à simulação; nenhum dado bancário é solicitado e nenhuma cobrança é realizada.
- **Entrega:** a sequência de status é **Recebido → Em preparo → Saiu para entrega → Entregue**. O administrador realiza as atualizações, que ficam no histórico.
- **Avaliações:** o cliente pode informar nota de 1 a 5 e comentário; existe apenas uma avaliação por combinação de usuário e produto, passível de edição pelo autor.
- **Relatórios:** as informações representam pedidos com pagamentos fictícios aprovados, não faturamento real da loja.

## 5. Requisitos de qualidade considerados

O sistema foi organizado para funcionar em computador e celular, apresentar mensagens compreensíveis de erro e sucesso, permitir navegação por teclado e preservar a integridade das informações gravadas. A separação entre interface, regras de negócio e banco de dados facilita futuras alterações e a realização de testes.

Foram previstos e registrados testes de acesso, carrinho, compras, consultas, administração e usabilidade básica. Metas originais de desempenho em segundos **não foram medidas como garantia de serviço**.

## 6. Adaptações e limites assumidos

| Pedido inicial | Tratamento adotado |
| --- | --- |
| Aplicativo mobile | Aplicação web responsiva acessível pelo navegador. |
| E-mail de confirmação | Mensagem de confirmação no sistema; envio externo não implementado. |
| Pagamento por gateway | Simulação de PIX/cartão, sem movimentação financeira. |
| Localização e entrega em tempo real | Histórico de status com atualização manual e endereço informado no pedido. |
| Fotos e catálogo definitivo | Imagens e produtos demonstrativos. |
| Impostos e frete | Não são calculados por ausência de regras comerciais e fiscais definidas. |
| Avaliações e relatórios | Desenvolvidos como recursos complementares. A aparência final dos PDFs requer revisão visual. |

Esses limites não devem ser apresentados como atendimento literal das integrações originalmente sugeridas. A primeira versão é adequada à demonstração do funcionamento do sistema, sem representar a operação comercial de uma loja real.
