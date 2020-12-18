﻿using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using VoxelTG.Player.Inventory;
using VoxelTG.Terrain;
using VoxelTG.UI;

namespace VoxelTG.DebugUtils
{
    [RequireComponent(typeof(DebugConsole))]
    public class DebugCommandHandler : MonoBehaviour
    {
        public const char COMMAND_PREFIX = '/';

        private DebugConsole debugConsole;

        [SerializeField] private KeyCode openInputFieldKeybind = KeyCode.Return;
        [SerializeField] private TMP_InputField inputField;

        private void Start()
        {
            debugConsole = GetComponent<DebugConsole>();
            inputField.onSubmit.AddListener(HandleCommands);
        }

        private void Update()
        {
            if (Input.GetKeyDown(openInputFieldKeybind))
            {
                // after sending message
                if (inputField.gameObject.activeSelf && inputField.text.Equals(string.Empty))
                {
                    inputField.gameObject.SetActive(false);
                }
                else
                {
                    inputField.gameObject.SetActive(true);
                    inputField.Select();
                    inputField.ActivateInputField();
                }
            }
        }

        private void HandleCommands(string msg)
        {
            inputField.SetTextWithoutNotify(string.Empty);
            EventSystem.current.SetSelectedGameObject(null);

            if (msg.Length == 0 || msg[0] != COMMAND_PREFIX)
                return;

            string[] content = msg.Split(' ');
            // remove COMMAND_PREFIX
            content[0] = content[0].Remove(0, 1);

            // /give ITEM [count = 1]
            if (content[0] == "get" && content.Length > 1)
            {
                ItemType itemType;
                BlockType blockType;
                int count = 1;

                if (Enum.TryParse(content[1], true, out blockType))
                    itemType = ItemType.MATERIAL;
                else if (Enum.TryParse(content[1], true, out itemType))
                    blockType = BlockType.AIR;
                else
                    return;

                // try get count
                if (content.Length >= 3)
                    int.TryParse(content[2], out count);

                if (itemType == ItemType.MATERIAL)
                    UIManager.InventoryUI.AddItemToInventory(new InventoryItemData(blockType, count));
                else if (itemType != ItemType.NONE)
                    UIManager.InventoryUI.AddItemToInventory(new InventoryItemData(itemType, count));
            }
        }
    }
}
