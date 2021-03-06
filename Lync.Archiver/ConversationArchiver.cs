﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Lync.Model;
using Microsoft.Lync.Model.Conversation;
using Microsoft.Lync.Model.Conversation.AudioVideo;

namespace Lync.Archiver
{
    public class ConversationArchiver : IDisposable
    {
        private ConversationManager converMgr;
        private Dictionary<string, ConversationContext> conversationContent;
        private bool disposed;

        public ConversationArchiver()
        {
            if (!Directory.Exists(Configuration.FileArchivePath))
            {
                Directory.CreateDirectory(Configuration.FileArchivePath);
            }
            try
            {
                LyncClient lyncClient = LyncClient.GetClient();
                if (lyncClient != null)
                {
                    converMgr = lyncClient.ConversationManager;
                    converMgr.ConversationAdded += conversation_ConversationAdded;

                    conversationContent = new Dictionary<string, ConversationContext>();
                }
            }
            catch (Exception exp)
            {
                throw new ApplicationException("Unable to get Lync Client.", exp);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~ConversationArchiver()
        {
            Dispose(false);
        }
        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    if (converMgr != null)
                    {
                        converMgr.ConversationAdded -= conversation_ConversationAdded;
                        converMgr = null;
                    }
                    conversationContent = null;
                    disposed = true;
                }

            }
        }

        private string calculateKey(IList<Participant> participants)
        {
            var convKey = String.Empty;
            for (var index = 1; index < participants.Count; index++)
            {
                convKey +=
                    (string) (participants[index].Contact.GetContactInformation(ContactInformationType.DisplayName));
            }
            return convKey;
        }

        private void conversation_ConversationAdded(object sender, ConversationManagerEventArgs e)
        {
            var conversation = e.Conversation;
            conversation.ParticipantAdded += conversation_ParticipantAdded;
            conversation.ParticipantRemoved += conversation_ParticipantRemoved;

            // Available Modalities 
            // None = 0,
            // InstantMessage = 1,
            // AudioVideo = 2,
            // Reserved1 = 4,
            // Reserved2 = 8,
            // Invalid = -1,

            // We use InstantMessage and AudioVideo
            if (e.Conversation.Modalities.ContainsKey(ModalityTypes.InstantMessage) &&
                e.Conversation.Modalities[ModalityTypes.InstantMessage] != null)
            {
                var imModality = (InstantMessageModality) e.Conversation.Modalities[ModalityTypes.InstantMessage];
                imModality.InstantMessageReceived += conversation_InstantMessageSent;
            }

            if (e.Conversation.Modalities.ContainsKey(ModalityTypes.AudioVideo) &&
    e.Conversation.Modalities[ModalityTypes.AudioVideo] != null)
            {
                var avModality = (AVModality)e.Conversation.Modalities[ModalityTypes.AudioVideo];
                avModality.ModalityStateChanged += av_ModalityStateChanged;
            }

            conversation.StateChanged += conversation_StateChanged;
        }

        private void conversation_InstantMessageSent(object sender, MessageSentEventArgs e)
        {
            handleImAction(sender, e);
        }

        private void conversation_InstantMessageReceived(object sender, MessageSentEventArgs e)
        {
            handleImAction(sender, e);
        }

// ReSharper disable once InconsistentNaming
        private void handleImAction(object sender, MessageSentEventArgs e)
        {
            var modality = sender as InstantMessageModality;

            if (modality == null)
                return;

            var participants = modality.Conversation.Participants;

            var name = (string) modality.Participant.Contact.GetContactInformation(ContactInformationType.DisplayName);
            var convKey = calculateKey(participants);
            update_Conversation(convKey, name, e.Text);
        }

        private void update_Conversation(string convKey,string name, string verse)
        {
            if (conversationContent.ContainsKey(convKey))
            {
                var onGoingConv = conversationContent[convKey];
                onGoingConv.AddVerse(name, verse);
            }
            else
            {
                var newConv = new ConversationContext();
                newConv.AddVerse(name, verse);
                conversationContent.Add(convKey, newConv);
            }
        }
        private void conversation_StateChanged(object sender, ConversationStateChangedEventArgs e)
        {
            if (e.NewState != ConversationState.Terminated)
                return;

            var conversation = sender as Conversation;

            if (conversation == null)
                return;

            var participants = conversation.Participants;
            var convKey = calculateKey(participants);

            try
            {
                if (conversationContent.ContainsKey(convKey))
                {
                    var convItem = conversationContent[convKey];

                    var archivers = ArchiveHelper.GetArchivers();
                    foreach (var arcer in archivers)
                    {
                        var arcer1 = arcer;
                        Parallel.Invoke(() => arcer1.Save(convKey, convItem));
                    }
                }
            }
            finally
            {
                conversationContent.Remove(convKey);
                conversation.ParticipantAdded += conversation_ParticipantAdded;
                conversation.ParticipantRemoved += conversation_ParticipantRemoved;

                if (conversation.Modalities.ContainsKey(ModalityTypes.InstantMessage) &&
                    conversation.Modalities[ModalityTypes.InstantMessage] != null)
                {
                    var imModality = (InstantMessageModality) conversation.Modalities[ModalityTypes.InstantMessage];
                    imModality.InstantMessageReceived -= conversation_InstantMessageSent;
                }

                conversation.StateChanged -= conversation_StateChanged;
            }
        }

        private void conversation_ParticipantAdded(object sender, ParticipantCollectionChangedEventArgs e)
        {
            var otherParty = e.Participant;
            
            if (e.Participant.IsSelf) return;

            if (!otherParty.Modalities.ContainsKey(ModalityTypes.InstantMessage) ||
                otherParty.Modalities[ModalityTypes.InstantMessage] == null) return;
            var imModality = (InstantMessageModality) otherParty.Modalities[ModalityTypes.InstantMessage];
            imModality.InstantMessageReceived += conversation_InstantMessageReceived;
        }

        private void conversation_ParticipantRemoved(object sender, ParticipantCollectionChangedEventArgs e)
        {
            var otherParty = e.Participant;
            
            if (e.Participant.IsSelf) return;
            
            if (!otherParty.Modalities.ContainsKey(ModalityTypes.InstantMessage) ||
                otherParty.Modalities[ModalityTypes.InstantMessage] == null) return;
            var imModality = (InstantMessageModality) otherParty.Modalities[ModalityTypes.InstantMessage];
            imModality.InstantMessageReceived -= conversation_InstantMessageReceived;
        }

        private void av_ModalityStateChanged(object sender, ModalityStateChangedEventArgs e)
        {
            var modality = sender as AVModality;

            if (modality == null)
                return;

            var participants = modality.Conversation.Participants;

            var name = (string) modality.Participant.Contact.GetContactInformation(ContactInformationType.DisplayName);
            var convKey = calculateKey(participants);

            var avStatus = e.NewState.ToString();
            update_Conversation(convKey, name, avStatus);
        }
    }
}