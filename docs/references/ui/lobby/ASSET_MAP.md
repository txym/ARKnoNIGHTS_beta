# LAN Lobby UI asset map

## Approved source roots

- **Unpacked room-select source:** `G:\素材\11.14\Unpacked_1763129662\Android\ui\autochess\[uc]autochessouter`. Home `room_select_` entries use only the normal filename; every Unpacked `$0` variant is forbidden.
- **Combined avatar source:** `G:\素材\11.14\Combined_1763139377\Android\ui\autochess\[uc]autochesscommon`. Home imports exactly the normal `icon_amiy`, `icon_clementi`, `icon_kirar`, and `icon_zumam` PNGs.

All root `UI/Lobby` rows use an `Unpacked direct` normal filename from the approved Unpacked root. The importer copies only its explicit whitelist; EditMode provenance tests byte-compare every root lobby PNG to its declared SHA-256 and source path.

| Imported file | Source-relative path | Resources destination | Visible role | Stretch mode | SHA-256 | Source kind |
| --- | --- | --- | --- | --- | --- | --- |
| bg_terrain.png | [uc]autochessouter/bg_terrain.png | UI/Lobby/bg_terrain | Lobby terrain background | Fill | ECE7B6159268276287C20E3B3A82A5165BCC1D344EDFA6DE3B88EE24A76F988C | Unpacked direct |
| shallow_main.png | [uc]autochessouter/shallow_main.png | UI/Lobby/shallow_main | Lobby terrain foreground | Fill | 054110DDEE56F1D19FAFA846D821E6CBD83D47BEB70D4C399A84DA4035A11945 | Unpacked direct |
| room_create_btn_bg.png | [uc]autochessouter/room_create_btn_bg.png | UI/Lobby/room_create_btn_bg | Create-room button background | Nine-slice | 2782FDCBE671CDD760FF46FD2B3A83CEB3F6396BC44FF48673220C8B21521606 | Unpacked direct |
| room_join_btn_bg.png | [uc]autochessouter/room_join_btn_bg.png | UI/Lobby/room_join_btn_bg | Join-room button background | Nine-slice | 38C41C0997D76056E3491E6E7D58F47F90A2CD907A3B359E26989838A7AD6A11 | Unpacked direct |
| create_icon.png | [uc]autochessouter/create_icon.png | UI/Lobby/create_icon | Create-room action icon | Preserve | AE047958EE4E7D43F368D3205110307200D30F79BCF393B36CBBDE37A8A00BA7 | Unpacked direct |
| join_icon.png | [uc]autochessouter/join_icon.png | UI/Lobby/join_icon | Join-room action icon | Preserve | 6DE45E7D0AF9FFAE47D271E6A719FE008412FF62704154D277788EA094381C09 | Unpacked direct |
| img_player_bkg.png | [uc]autochessouter/img_player_bkg.png | UI/Lobby/img_player_bkg | Player card background | Nine-slice | CB940ECA5FE84527D9AD4546C617120A90F94CD88663F98C66261EE60D45185A | Unpacked direct |
| img_player_confirmed.png | [uc]autochessouter/img_player_confirmed.png | UI/Lobby/img_player_confirmed | Confirmed player indicator | Preserve | 921FC9B33FFC7CBA6D90E6CA3CEF29145DC05792B8A1B3C2399DAAEA8192CFB5 | Unpacked direct |
| player_card_waiting.png | [uc]autochessouter/player_card_waiting.png | UI/Lobby/player_card_waiting | Waiting player card state | Nine-slice | 3B87EBB1DD62B7A8BD4D525F7F2E7E4358F2F1C0B04A65997BA34E79E97F0C8C | Unpacked direct |
| player_card_ready.png | [uc]autochessouter/player_card_ready.png | UI/Lobby/player_card_ready | Ready player card state | Nine-slice | F34786A3E832E97121EB03614B6D584C871B78E4C5CDD1FA6C5F9CB7D191A4C0 | Unpacked direct |
| player_card_self_frame.png | [uc]autochessouter/player_card_self_frame.png | UI/Lobby/player_card_self_frame | Local player card frame | Nine-slice | 19F0D43B704F9CB92EE3BE11D9C64879D1BA9B542EDFF90E9DBDF7FCE381C3A3 | Unpacked direct |
| team_icon_frame.png | [uc]autochessouter/team_icon_frame.png | UI/Lobby/team_icon_frame | Team icon frame | Preserve | B05BFEAE52C1E54E9382936F653E291D118A976DA0B1AA6DE14720FC9CCC4380 | Unpacked direct |
| team_hp_back.png | [uc]autochessouter/team_hp_back.png | UI/Lobby/team_hp_back | Team HP background | Horizontal fill | 6987D8B1428A3F51C9F4697B014A76515CF51130E74991E44FE660E838602E74 | Unpacked direct |
| btn_match_host_normal.png | [uc]autochessouter/btn_match_host_normal.png | UI/Lobby/btn_match_host_normal | Host start-match button | Nine-slice | 9D36CBDA42FC64CEB7590CBEDF5E49176BFA90E63A8B263FD3C87EE08C3BF3DF | Unpacked direct |
| btn_match_host_grey.png | [uc]autochessouter/btn_match_host_grey.png | UI/Lobby/btn_match_host_grey | Disabled host start-match button | Nine-slice | C4CD3326EA4D04777AAA540525405DF8AA217D2E972443FDE95C202333F93614 | Unpacked direct |
| btn_match_grey.png | [uc]autochessouter/btn_match_grey.png | UI/Lobby/btn_match_grey | Disabled guest match button | Nine-slice | E774CB0533EB67BD2FE45F50339E36D0A6221BAF594E6AEE5E5C256469A4BA78 | Unpacked direct |
| btn_match_cancel.png | [uc]autochessouter/btn_match_cancel.png | UI/Lobby/btn_match_cancel | Cancel-match button | Nine-slice | 6DA0D4FD99A7A3595F5FE3C79ABA99A1F41006A4114E7555D324E059419A97B3 | Unpacked direct |
| card_bg.png | [uc]autochessouter/card_bg.png | UI/Lobby/card_bg | Room player card background | Nine-slice | 050B347451BBEBC74F5E3B09A2470931D9B2A85DEF707A4AAC42B5CE1B0BCEE2 | Unpacked direct |
| bg_top_normal.png | [uc]autochessouter/bg_top_normal.png | UI/Lobby/bg_top_normal | Room slot normal top bar | Preserve | 5A9479B9AFDD4FC3F597CCBF4A1A0D92C1BB1B5053D716E8C20267E6B2C77C4F | Unpacked direct |
| bg_top_ready.png | [uc]autochessouter/bg_top_ready.png | UI/Lobby/bg_top_ready | Ready room slot top bar | Preserve | EFAA99906A087AAF5AD631E4DF8CFCD7E90C4F463621779A13447675F221482D | Unpacked direct |
| card_empty.png | [uc]autochessouter/card_empty.png | UI/Lobby/card_empty | Empty room slot card | Nine-slice | 4DD34E0B5BE318770082B14F245591D80F6ABFF00451744C4BEF3459798DCE31 | Unpacked direct |
| card_deco_bg.png | [uc]autochessouter/card_deco_bg.png | UI/Lobby/card_deco_bg | Neutral room slot lower decoration | Preserve | C907B3527747B947ECCA08757DD6601BCA46B8EC5AF835F61F226BBBF8E1EBF1 | Unpacked direct |
| card_deco_self.png | [uc]autochessouter/card_deco_self.png | UI/Lobby/card_deco_self | Local player card decoration | Preserve | A3217A0EE5C8B1D7325758162C9859BEC765C7B63331881C90CDA4804A93F661 | Unpacked direct |
| bg_plus.png | [uc]autochessouter/bg_plus.png | UI/Lobby/bg_plus | Empty room slot add marker | Preserve | E2CA5554B27862FE172E2D18D50092618B2E895C2AD63CDB57019BE593B7B66D | Unpacked direct |
| btn_match_normal.png | [uc]autochessouter/btn_match_normal.png | UI/Lobby/btn_match_normal | Room start-match button | Nine-slice | 62B586274488AE3A7BF203829DDFE0C80955993AE22334F46EDE076215C3ADCD | Unpacked direct |
| btn_topmenu_back.png | [uc]autochessouter/btn_topmenu_back.png | UI/Lobby/btn_topmenu_back | Room top-menu back button | Preserve | BB78B1FCB84BA5F3A2FF8992809C8B0EFD4CAC5E1E960A8056BAA79A1A6E6303 | Unpacked direct |
| img_return.png | [uc]autochessouter/img_return.png | UI/Lobby/img_return | Room square return action | Exact | 3F20542913541EAF1F175225268FD3E1EC0343C45A18D0BFE3F7DFBDDEFBEC09 | Unpacked direct |
| host_top_tag.png | [uc]autochessouter/host_top_tag.png | UI/Lobby/host_top_tag | Host room slot top tag | Preserve | 861754CAFABFEF6641129CAC439501EE3E3D964E0FA3C72BC32FDAC117131009 | Unpacked direct |
| room_select_right_bg.png | [uc]autochessouter/room_select_right_bg.png | UI/Lobby/Home/room_select_right_bg | Home right-side background | Preserve |
| room_select_title_icon.png | [uc]autochessouter/room_select_title_icon.png | UI/Lobby/Home/room_select_title_icon | Home title ornament | Preserve |
| room_select_dot.png | [uc]autochessouter/room_select_dot.png | UI/Lobby/Home/room_select_dot | Home dot ornament | Preserve |
| room_select_img_startroom.png | [uc]autochessouter/room_select_img_startroom.png | UI/Lobby/Home/room_select_img_startroom | Home room-start decoration | Preserve |
| room_select_create_btn_bg_down.png | [uc]autochessouter/room_select_create_btn_bg_down.png | UI/Lobby/Home/room_select_create_btn_bg_down | Create action background | Nine-slice |
| room_select_create_left_line.png | [uc]autochessouter/room_select_create_left_line.png | UI/Lobby/Home/room_select_create_left_line | Create section line | Preserve |
| room_select_create_logo.png | [uc]autochessouter/room_select_create_logo.png | UI/Lobby/Home/room_select_create_logo | Create section logo | Preserve |
| room_select_create_middleicon.png | [uc]autochessouter/room_select_create_middleicon.png | UI/Lobby/Home/room_select_create_middleicon | Create middle ornament | Preserve |
| room_select_create_text_01.png | [uc]autochessouter/room_select_create_text_01.png | UI/Lobby/Home/room_select_create_text_01 | Create label ornament 01 | Preserve |
| room_select_create_text_02.png | [uc]autochessouter/room_select_create_text_02.png | UI/Lobby/Home/room_select_create_text_02 | Create label ornament 02 | Preserve |
| room_select_join_ban.png | [uc]autochessouter/room_select_join_ban.png | UI/Lobby/Home/room_select_join_ban | Join unavailable ornament | Preserve |
| room_select_join_blank.png | [uc]autochessouter/room_select_join_blank.png | UI/Lobby/Home/room_select_join_blank | Join code blank | Nine-slice |
| room_select_join_btn_bg_down.png | [uc]autochessouter/room_select_join_btn_bg_down.png | UI/Lobby/Home/room_select_join_btn_bg_down | Join action background | Nine-slice |
| room_select_join_left_block.png | [uc]autochessouter/room_select_join_left_block.png | UI/Lobby/Home/room_select_join_left_block | Join left block | Preserve |
| room_select_join_logo.png | [uc]autochessouter/room_select_join_logo.png | UI/Lobby/Home/room_select_join_logo | Join section logo | Preserve |
| room_select_join_middle_block.png | [uc]autochessouter/room_select_join_middle_block.png | UI/Lobby/Home/room_select_join_middle_block | Join middle block | Preserve |
| room_select_join_middle_block_mask.png | [uc]autochessouter/room_select_join_middle_block_mask.png | UI/Lobby/Home/room_select_join_middle_block_mask | Join middle mask | Preserve |
| room_select_join_right_block.png | [uc]autochessouter/room_select_join_right_block.png | UI/Lobby/Home/room_select_join_right_block | Join right block | Preserve |
| room_select_join_text_01.png | [uc]autochessouter/room_select_join_text_01.png | UI/Lobby/Home/room_select_join_text_01 | Join label ornament 01 | Preserve |
| room_select_join_text_02.png | [uc]autochessouter/room_select_join_text_02.png | UI/Lobby/Home/room_select_join_text_02 | Join label ornament 02 | Preserve |
| room_select_join_text_bg.png | [uc]autochessouter/room_select_join_text_bg.png | UI/Lobby/Home/room_select_join_text_bg | Join label background | Preserve |
| room_select_join_triangle.png | [uc]autochessouter/room_select_join_triangle.png | UI/Lobby/Home/room_select_join_triangle | Join direction ornament | Preserve |
| img_pointer.png | [uc]autochessouter/img_pointer.png | UI/Lobby/Home/img_pointer | Create tapered wing segment | Preserve |
| doc_frame_line.png | [uc]autochessouter/doc_frame_line.png | UI/Lobby/Home/doc_frame_line | Create outline segment | Preserve |
| icon_amiy.png | Combined/[uc]autochesscommon/icon_amiy.png | UI/Lobby/Home/icon_amiy | Home avatar option | Preserve |
| icon_clementi.png | Combined/[uc]autochesscommon/icon_clementi.png | UI/Lobby/Home/icon_clementi | Home avatar option | Preserve |
| icon_kirar.png | Combined/[uc]autochesscommon/icon_kirar.png | UI/Lobby/Home/icon_kirar | Home avatar option | Preserve |
| icon_zumam.png | Combined/[uc]autochesscommon/icon_zumam.png | UI/Lobby/Home/icon_zumam | Home avatar option | Preserve |
