using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ChatServerWinForms
{
    public partial class Form1 : Form
    {
        private TcpListener server = null;
        private bool isServerRunning = false;
        private CancellationTokenSource cts = null;

        // Aqui podemos aproveitar a mesma estrutura de canais do exemplo anterior
        private Dictionary<string, ChatChannel> channels = new Dictionary<string, ChatChannel>();

        public Form1()
        {
            InitializeComponent();
        }

        // --- BOTÃO LIGAR ---
        private async void btnLigar_Click(object sender, EventArgs e)
        {
            if (!isServerRunning)
            {
                try
                {
                    // Define como "ligado"
                    isServerRunning = true;
                    cts = new CancellationTokenSource();

                    // Inicia o servidor na porta 5000 (pode mudar se quiser)
                    int port = 5000;
                    server = new TcpListener(IPAddress.Any, port);
                    server.Start();

                    Log($"[Servidor] Iniciado na porta {port}.");

                    // Aceita conexões em segundo plano (Task)
                    await Task.Run(() => AcceptClients(cts.Token));
                }
                catch (Exception ex)
                {
                    Log($"[Erro] Não foi possível iniciar o servidor: {ex.Message}");
                    isServerRunning = false;
                }
            }
            else
            {
                Log("[Aviso] Servidor já está em execução.");
            }
        }


        // --- MÉTODO PRINCIPAL DE ACEITAR CLIENTES ---
        private void AcceptClients(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (server.Pending()) // se há clientes tentando conectar
                    {
                        TcpClient client = server.AcceptTcpClient();
                        Log($"[Servidor] Novo cliente conectado de {client.Client.RemoteEndPoint}.");

                        // Cria uma thread ou Task para lidar com o cliente
                        ThreadPool.QueueUserWorkItem(HandleClient, client);
                    }
                    else
                    {
                        // Pequena pausa para evitar loop ocupado
                        Thread.Sleep(100);
                    }
                }
            }
            catch (Exception ex)
            {
                // Se o servidor parar, normalmente dá exceção no AcceptTcpClient
                Log($"[Servidor] Encerrado. Motivo: {ex.Message}");
            }
        }

        // --- LIDAR COM O CLIENTE (COMANDOS, MENSAGENS, ETC.) ---
        private void HandleClient(object obj)
        {
            TcpClient client = (TcpClient)obj;
            NetworkStream stream = null;
            string currentChannelName = null;

            try
            {
                stream = client.GetStream();
                byte[] buffer = new byte[1024];
                bool connected = true;

                while (connected && isServerRunning)
                {
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead == 0) // cliente desconectou
                    {
                        connected = false;
                        break;
                    }

                    string message = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();

                    // Verifica se é comando
                    if (message.StartsWith("/"))
                    {
                        string[] parts = message.Split(' ');
                        string command = parts[0].ToLower();

                        switch (command)
                        {
                            case "/create":
                                if (parts.Length < 2)
                                {
                                    SendPrivateMessage(stream, "Uso: /create <canal>");
                                }
                                else
                                {
                                    string channelName = parts[1];
                                    CreateChannel(channelName);
                                    SendPrivateMessage(stream, $"Canal '{channelName}' criado.");
                                }
                                break;

                            case "/delete":
                                if (parts.Length < 2)
                                {
                                    SendPrivateMessage(stream, "Uso: /delete <canal>");
                                }
                                else
                                {
                                    string channelName = parts[1];
                                    DeleteChannel(channelName);
                                    SendPrivateMessage(stream, $"Canal '{channelName}' excluído (se existia).");
                                }
                                break;

                            case "/join":
                                if (parts.Length < 2)
                                {
                                    SendPrivateMessage(stream, "Uso: /join <canal>");
                                }
                                else
                                {
                                    string channelName = parts[1];
                                    // Sai do canal atual
                                    if (!string.IsNullOrEmpty(currentChannelName))
                                    {
                                        RemoveClientFromChannel(currentChannelName, client);
                                    }
                                    // Entra no novo
                                    JoinChannel(channelName, client);
                                    currentChannelName = channelName;
                                    SendPrivateMessage(stream, $"Você entrou no canal '{channelName}'.");
                                }
                                break;

                            case "/exit":
                                SendPrivateMessage(stream, "Você saiu do servidor.");
                                connected = false;
                                break;

                            default:
                                SendPrivateMessage(stream, "Comando não reconhecido.");
                                break;
                        }
                    }
                    else
                    {
                        // Mensagem normal: broadcast para o canal atual
                        if (!string.IsNullOrEmpty(currentChannelName))
                        {
                            BroadcastToChannel(currentChannelName, message, client);
                        }
                        else
                        {
                            SendPrivateMessage(stream, "Você não está em nenhum canal. Use /join <canal> primeiro.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[Erro] Cliente: {ex.Message}");
            }
            finally
            {
                // Sai do canal atual
                if (!string.IsNullOrEmpty(currentChannelName))
                {
                    RemoveClientFromChannel(currentChannelName, client);
                }
                client.Close();
                Log("[Cliente] Desconectado.");
            }
        }

        // --- CRIAR CANAL ---
        private void CreateChannel(string channelName)
        {
            lock (channels)
            {
                if (!channels.ContainsKey(channelName))
                {
                    channels[channelName] = new ChatChannel(channelName);
                    Log($"[Servidor] Canal '{channelName}' criado.");

                    // Atualiza a ListBox de canais
                    AtualizarListaCanais();
                }
                else
                {
                    Log($"[Servidor] Canal '{channelName}' já existe.");
                }
            }
        }

        // --- EXCLUIR CANAL ---
        private void DeleteChannel(string channelName)
        {
            lock (channels)
            {
                if (channels.ContainsKey(channelName))
                {
                    channels.Remove(channelName);
                    Log($"[Servidor] Canal '{channelName}' excluído.");
                }
                else
                {
                    Log($"[Servidor] Canal '{channelName}' não existe.");
                }
            }
        }

        // --- ENTRAR NO CANAL ---
        private void JoinChannel(string channelName, TcpClient client)
        {
            lock (channels)
            {
                if (!channels.ContainsKey(channelName))
                {
                    // Cria automaticamente se não existir
                    channels[channelName] = new ChatChannel(channelName);
                    Log($"[Servidor] Canal '{channelName}' criado automaticamente (join).");
                }
                channels[channelName].AddClient(client);
            }
        }

        // --- REMOVER CLIENTE DE UM CANAL ---
        private void RemoveClientFromChannel(string channelName, TcpClient client)
        {
            lock (channels)
            {
                if (channels.ContainsKey(channelName))
                {
                    channels[channelName].RemoveClient(client);
                }
            }
        }

        // --- ENVIAR MENSAGEM PARA O CANAL ---
        private void BroadcastToChannel(string channelName, string message, TcpClient sender)
        {
            lock (channels)
            {
                if (channels.ContainsKey(channelName))
                {
                    channels[channelName].BroadcastMessage(message, sender);
                }
            }
        }

        // --- ENVIAR MENSAGEM PRIVADA PARA UM CLIENTE ---
        private void SendPrivateMessage(NetworkStream stream, string message)
        {
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(message + Environment.NewLine);
                stream.Write(data, 0, data.Length);
            }
            catch { /* tratar erro se quiser */ }
        }

        // --- MÉTODO PARA LOGAR MENSAGENS NA LISTBOX (OU TEXTBOX) ---
        private void Log(string text)
        {
            // Se estiver em thread separada, precisamos invocar na UI
            if (InvokeRequired)
            {
                Invoke(new Action<string>(Log), text);
            }
            else
            {
                listBoxLog.Items.Add(text);
                // Auto-scroll se quiser
                listBoxLog.TopIndex = listBoxLog.Items.Count - 1;
            }
        }

        private void btnParar_Click_1(object sender, EventArgs e)
        {

            if (isServerRunning)
            {
                isServerRunning = false;
                cts.Cancel(); // sinaliza para encerrar AcceptClients
                server?.Stop();
                Log("[Servidor] Parado.");
            }
            else
            {
                Log("[Aviso] Servidor não está em execução.");
            }
        }

        private void listBoxLog_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void btnAddChannel_Click_1(object sender, EventArgs e)
        {
            string channelName = txtChannelName.Text.Trim();
            if (!string.IsNullOrEmpty(channelName))
            {
                CreateChannel(channelName);
            }
            else
            {
                Log("[Aviso] Informe o nome do canal para adicionar.");
            }
        }

        private void btnDeleteChannel_Click_1(object sender, EventArgs e)
        {

            string channelName = txtChannelName.Text.Trim();
            if (!string.IsNullOrEmpty(channelName))
            {
                DeleteChannel(channelName);
            }
            else
            {
                Log("[Aviso] Informe o nome do canal para excluir.");
            }
        }
        private void AtualizarListaCanais()
        {
            if (InvokeRequired) // Garante que a UI seja atualizada corretamente em threads diferentes
            {
                Invoke(new Action(AtualizarListaCanais));
                return;
            }

            lstChannels.Items.Clear(); // Limpa a ListBox
            lock (channels)
            {
                foreach (var canal in channels.Keys)
                {
                    lstChannels.Items.Add(canal); // Adiciona os canais na ListBox
                }
            }
        }

        private void lstChannels_SelectedIndexChanged(object sender, EventArgs e)
        {

        }
    }

    // --------------------------------------------------
    // Classe que representa um canal de chat
    // --------------------------------------------------
    public class ChatChannel
    {
        public string Name { get; private set; }
        private List<TcpClient> clients = new List<TcpClient>();

        public ChatChannel(string name)
        {
            Name = name;
        }

        // Adiciona cliente ao canal
        public void AddClient(TcpClient client)
        {
            lock (clients)
            {
                if (!clients.Contains(client))
                {
                    clients.Add(client);
                }
            }
        }

        // Remove cliente do canal
        public void RemoveClient(TcpClient client)
        {
            lock (clients)
            {
                if (clients.Contains(client))
                {
                    clients.Remove(client);
                }
            }
        }

        // Envia a mensagem para todos os clientes do canal, exceto quem enviou
        public void BroadcastMessage(string message, TcpClient sender)
        {
            lock (clients)
            {
                foreach (var client in clients)
                {
                    if (client != sender)
                    {
                        try
                        {
                            NetworkStream stream = client.GetStream();
                            byte[] data = Encoding.UTF8.GetBytes(message + Environment.NewLine);
                            stream.Write(data, 0, data.Length);
                        }
                        catch
                        {
                            // Se der erro, pode ignorar ou remover o cliente da lista
                        }
                    }
                }
            }

        }


    }


}
