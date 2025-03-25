using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GCliente
{
    public partial class Form1 : Form
    {

        private TcpClient client;
        private NetworkStream stream;


        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {

        }

        private  async void btnConectar_Click(object sender, EventArgs e)
        {
            try
            {
                string ip = txtIP.Text.Trim();
                int port = int.Parse(txtPort.Text.Trim());
                string username = txtNome.Text.Trim(); // Pega o nome digitado

                if (string.IsNullOrEmpty(username))
                {
                    MessageBox.Show("Digite um nome de usuário!", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                client = new TcpClient();
                await client.ConnectAsync(ip, port);
                stream = client.GetStream();

                // Envia o nome de usuário para o servidor
                byte[] data = Encoding.UTF8.GetBytes(username);
                await stream.WriteAsync(data, 0, data.Length);

                AppendMessage($"Conectado como {username}");

                // Inicia a escuta de mensagens do servidor
                Task.Run(() => ReceberMensagens());
            }
            catch (Exception ex)
            {
                AppendMessage($"Erro ao conectar: {ex.Message}");
            }
        }

        private void ReceberMensagens()
        {
            try
            {
                byte[] buffer = new byte[1024];
                while (client.Connected)
                {
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead == 0)
                    {
                        AppendMessage("Desconectado pelo servidor.");
                        break;
                    }
                    string mensagem = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    AppendMessage($"Servidor: {mensagem}");
                }
            }
            catch (Exception ex)
            {
                AppendMessage($"Erro ao receber mensagem: {ex.Message}");
            }
        }


        // Método auxiliar para atualizar a interface (precisa de Invoke se chamado de outra thread)
        private void AppendMessage(string message)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => AppendMessage(message)));
            }
            else
            {
                // Você pode usar um TextBox ou ListBox para exibir as mensagens
                txtMensagens.AppendText(message + Environment.NewLine);
            }
        }

        private void btnEnviar_Click(object sender, EventArgs e)
        {
            if (client != null && client.Connected)
            {
                string mensagem = txtMensagemEnviar.Text.Trim();
                if (!string.IsNullOrEmpty(mensagem))
                {
                    byte[] data = Encoding.UTF8.GetBytes(mensagem);
                    stream.Write(data, 0, data.Length);
                    AppendMessage($"Você: {mensagem}");
                    txtMensagemEnviar.Clear();
                }
            }
            else
            {
                AppendMessage("Não está conectado a um servidor.");
            }
        }
    }
}

